using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Platform;
using Maze.Application.Services;
using Maze.Core.Common;
using UnityEngine;

namespace Maze.Application.Save
{
    /// <summary>Where the save JSON lives. Swapped for an in-memory one in tests.</summary>
    public interface ISaveStorage
    {
        /// <summary>Null when nothing is saved.</summary>
        string Read();

        void Write(string json);
    }

    /// <summary>
    /// Stores the save in <see cref="PlayerPrefs"/>: works the same on Android and in WebGL (IndexedDB),
    /// where files in persistentDataPath need extra syncing.
    /// </summary>
    public sealed class PlayerPrefsSaveStorage : ISaveStorage
    {
        public const string Key = "Maze.SaveData";

        public string Read() => PlayerPrefs.HasKey(Key) ? PlayerPrefs.GetString(Key) : null;

        public void Write(string json)
        {
            PlayerPrefs.SetString(Key, json);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// SaveData persistence (ТЗ §85). Loaded once at start-up; <see cref="Save"/> is called by owners of persistent
    /// events: level completion (progress) and settings changes. A broken save never stops the game: it is
    /// replaced by a fresh one and the problem is logged.
    /// With a platform's cloud (<see cref="ICloudSave"/>): the cloud save wins at start-up (it follows the player
    /// between devices and builds); an empty cloud gets the local save (migration). Every save is written locally at
    /// once and to the cloud after <see cref="CloudDelay"/> (changes in a row go as one write), at once when the game
    /// goes to the background (<see cref="FlushCloud"/>). A cloud that failed to answer at start-up is not written
    /// this session: an empty local save must not replace a real one there.
    /// </summary>
    public sealed class SaveService : IApplicationService, IDisposable
    {
        private readonly ISaveStorage _storage;
        private readonly PlatformService _platform;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private ICloudSave _cloud;
        private bool _cloudDirty;
        private bool _cloudLoop;

        public SaveService(ISaveStorage storage, PlatformService platform = null)
        {
            _storage = storage;
            _platform = platform;
        }

        public string Name => "Save";

        /// <summary>Current data; never null after initialization.</summary>
        public SaveData Data { get; private set; } = new SaveData();

        /// <summary>Saves go to the platform's cloud (it answered at start-up).</summary>
        public bool UsesCloud => _cloud != null;

        /// <summary>A cloud write waits this long for more changes (seconds, real time).</summary>
        public float CloudDelay { get; set; } = 2f;

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            var local = ReadSafe();
            var cloud = _platform?.CloudSave;
            if (cloud == null)
            {
                Load(local);
                return;
            }

            string remote;
            try
            {
                remote = await cloud.ReadAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Save, e, "Reading the cloud save failed; this session saves only locally");
                Load(local);
                return;
            }

            _cloud = cloud;
            var data = string.IsNullOrEmpty(remote) ? null : Parse(remote);
            if (data != null)
            {
                Data = data;
                Data.Normalize();
                WriteLocal(JsonUtility.ToJson(Data)); // the browser's copy follows the cloud
                GameLog.Info(LogChannel.Save, "Cloud save used.");
                return;
            }

            Load(local);
            if (!string.IsNullOrEmpty(local))
            {
                GameLog.Info(LogChannel.Save, "Cloud save is empty: the local save is moved there.");
                await WriteCloudAsync(JsonUtility.ToJson(Data));
            }
        }

        public void Load() => Load(ReadSafe());

        private void Load(string json)
        {
            Data = Parse(json) ?? new SaveData();
            Data.Normalize();
        }

        /// <summary>
        /// Writes <see cref="Data"/> locally and schedules the cloud write. False (and a logged error) when the local
        /// storage failed.
        /// </summary>
        public bool Save()
        {
            string json;
            try
            {
                json = JsonUtility.ToJson(Data);
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Save, e, "Saving failed");
                return false;
            }

            if (_cloud != null)
            {
                _cloudDirty = true;
                if (!_cloudLoop) CloudLoopAsync().Forget();
            }

            return WriteLocal(json);
        }

        /// <summary>
        /// After the player signed in: the cloud now holds the account's save. Its progress and this one are merged
        /// (<see cref="SaveData.MergeFrom"/>, settings stay these) and written to both places. A cloud that does not
        /// answer is not written (as at start-up). Returns true when the merged save was written to the cloud.
        /// </summary>
        public async UniTask<bool> MergeCloudAsync(CancellationToken cancellation)
        {
            var cloud = _platform?.CloudSave;
            if (cloud == null) return false;

            string remote;
            try
            {
                remote = await cloud.ReadAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Save, e, "Reading the account's cloud save failed");
                return false;
            }

            _cloud = cloud;
            var data = string.IsNullOrEmpty(remote) ? null : Parse(remote);
            if (data != null)
            {
                data.Normalize();
                Data.MergeFrom(data);
                GameLog.Info(LogChannel.Save, "Signed in: the account's save is merged with this one.");
            }

            var json = JsonUtility.ToJson(Data);
            WriteLocal(json);
            _cloudDirty = false;
            await WriteCloudAsync(json);
            return !_cloudDirty;
        }

        /// <summary>Writes a pending cloud save now (the game goes to the background: the tab may be closed).</summary>
        public void FlushCloud() => FlushCloudAsync().Forget();

        public UniTask FlushCloudAsync()
        {
            if (_cloud == null || !_cloudDirty) return UniTask.CompletedTask;
            _cloudDirty = false;
            return WriteCloudAsync(JsonUtility.ToJson(Data));
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private async UniTaskVoid CloudLoopAsync()
        {
            _cloudLoop = true;
            try
            {
                while (_cloudDirty)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(CloudDelay), DelayType.Realtime, PlayerLoopTiming.Update,
                        _lifetime.Token);
                    if (!_cloudDirty) break; // flushed meanwhile
                    _cloudDirty = false;
                    await WriteCloudAsync(JsonUtility.ToJson(Data));
                }
            }
            catch (OperationCanceledException)
            {
                // The application is closing.
            }
            finally
            {
                _cloudLoop = false;
            }
        }

        private async UniTask WriteCloudAsync(string json)
        {
            try
            {
                await _cloud.WriteAsync(json, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // The application is closing.
            }
            catch (Exception e)
            {
                _cloudDirty = true; // retried with the next save
                GameLog.Exception(LogChannel.Save, e, "Writing the cloud save failed");
            }
        }

        private bool WriteLocal(string json)
        {
            try
            {
                _storage.Write(json);
                return true;
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Save, e, "Saving failed");
                return false;
            }
        }

        /// <summary>Forgets all progress and settings.</summary>
        public void ResetAll()
        {
            Data = new SaveData();
            Save();
            GameLog.Info(LogChannel.Save, "Save data reset.");
        }

        private string ReadSafe()
        {
            try
            {
                return _storage.Read();
            }
            catch (Exception e)
            {
                GameLog.Exception(LogChannel.Save, e, "Reading the save failed");
                return null;
            }
        }

        private static SaveData Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                GameLog.Info(LogChannel.Save, "No save found: starting fresh.");
                return null;
            }

            try
            {
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null) throw new FormatException("Empty save.");
                GameLog.Info(LogChannel.Save, $"Save loaded: {data.UnlockedLevels?.Count ?? 0} unlocked level(s), " +
                                              $"{data.LevelStars?.Count ?? 0} with stars, {data.TotalKills} kill(s).");
                return data;
            }
            catch (Exception e)
            {
                GameLog.Warning(LogChannel.Save, $"Save is corrupted, starting fresh: {e.Message}");
                return null;
            }
        }
    }
}
