using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Application.Meta;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Common;
using Purge.Core.Audio;
using Purge.Presentation.Localization;
using UnityEngine;
namespace Purge.Presentation.UI
{
    public sealed class ArsenalPresenter : IDisposable
    {
        private readonly UIRoot _ui; private readonly ArsenalService _arsenal; private readonly SharedDefinitionsService _shared;
        private readonly LocalizationService _texts; private readonly IAddressablesService _assets; private readonly AudioService _audio;
        private readonly List<ContentDefinition> _items = new List<ContentDefinition>();
        private readonly List<string> _labels = new List<string>(); private readonly List<Sprite> _icons = new List<Sprite>();
        private readonly List<Rarity> _rarities = new List<Rarity>();
        private CancellationTokenSource _selection; private IAssetOwner _owner;
        private int _category, _selected; private bool _initialized;
        public bool IsOpen { get; private set; }
        public bool IsLoading { get; private set; }
        private WeaponDefinition _guidedPurchase;
        private readonly List<ContentDefinition> _catalogItems = new List<ContentDefinition>();

        public event Action BackRequested;
        public event Action GuidedPurchaseCompleted;

        public WeaponDefinition GuidedWeapon => _shared.Content?.Find<WeaponDefinition>(ArsenalService.TutorialPurchaseId);

        public bool CanBuyGuidedWeapon => _arsenal.CanBuy(GuidedWeapon);

        public bool HasAvailablePurchase()
        {
            _catalogItems.Clear();
            _shared.Content?.GetAll(_catalogItems);
            return _catalogItems.Exists(_arsenal.CanBuy);
        }
        public ArsenalPresenter(UIRoot ui, ArsenalService arsenal, SharedDefinitionsService shared, LocalizationService texts,
            IAddressablesService assets, AudioService audio) { _ui=ui; _arsenal=arsenal; _shared=shared; _texts=texts; _assets=assets; _audio=audio; }
        private ArsenalScreen View => _ui.Arsenal;
        public void Initialize()
        {
            if (_initialized || View == null) return; _initialized=true;
            View.UpgradeClicked += Upgrade; View.ForkClicked += Fork;
            View.BackClicked += Back; View.PreviousClicked += Previous; View.NextClicked += Next;
            View.Selected += Select; View.BuyClicked += Buy; View.FavoriteClicked += Favorite; View.ExcludeClicked += Exclude;
            _texts.LanguageChanged += LanguageChanged;
        }
        public void Open() => Open(false);

        public void Open(bool guidePurchase)
        {
            Initialize();
            if (View == null) return;
            _guidedPurchase = guidePurchase ? GuidedWeapon : null;
            if (_guidedPurchase != null) _category = (int)_guidedPurchase.ArsenalClass;
            IsOpen = true;
            View.SetVisible(true);
            Fill();
        }
        public void Close() { _guidedPurchase = null; IsOpen=false; ClearPreview(); if (View != null) View.SetVisible(false); }
        private void Back() { Close(); BackRequested?.Invoke(); }
        private void Previous() { if (_guidedPurchase != null) return; _category=(_category+8)%9; _selected=0; Fill(); }
        private void Next() { if (_guidedPurchase != null) return; _category=(_category+1)%9; _selected=0; Fill(); }
        private void LanguageChanged() { if (IsOpen) Fill(); }
        private string T(string key) => _texts.Get(key);
        private ContentDefinition Item => _items.Count > _selected ? _items[_selected] : null;
        private void Fill()
        {
            _items.Clear(); var all = new List<ContentDefinition>(); _shared.Content?.GetAll(all);
            foreach (var item in all)
                if (!ArsenalService.IsBase(item) && (item is WeaponDefinition w && (int)w.ArsenalClass == _category || item is ThrowableDefinition && _category==8)) _items.Add(item);
            _items.Sort((a,b) => { var r=RarityOf(a).CompareTo(RarityOf(b)); return r!=0?r:string.CompareOrdinal(a.Id,b.Id); });
            if (_guidedPurchase != null) _selected = _items.IndexOf(_guidedPurchase);
            _selected=Mathf.Clamp(_selected,0,Mathf.Max(0,_items.Count-1)); _labels.Clear(); _icons.Clear();
            _rarities.Clear();
            foreach (var item in _items)
            {
                var mark=_arsenal.Mark(item); var suffix=mark==PoolMark.Favorite?" ★":mark==PoolMark.Excluded?" ⊘":"";
                var rarity = RarityOf(item);
                _rarities.Add(rarity);
                _labels.Add(Name(item)+"\n"+T("arsenal.rarity."+(int)rarity)+suffix+(_arsenal.IsOpen(item)?"":" · "+T("arsenal.locked")));
                _icons.Add(item is WeaponDefinition w ? _shared.WeaponVisuals?.Find(w)?.Icon : _shared.ItemVisuals?.Find(item)?.Icon);
            }
            Select(_selected);
        }
        private string Name(ContentDefinition item) => T((item is WeaponDefinition?"weapon.":"throwable.")+item.Id);
        private static Rarity RarityOf(ContentDefinition item) => item is WeaponDefinition w?w.Rarity:((ThrowableDefinition)item).Rarity;
        private void Select(int index)
        {
            if (_guidedPurchase != null) index = _items.IndexOf(_guidedPurchase);
            _selected=index; View.List(T("arsenal.class."+_category),_arsenal.Money,_labels,_icons,_rarities,_selected); var item=Item; if(item==null) { ClearPreview(); return; }
            var open=_arsenal.IsOpen(item); var mark=_arsenal.Mark(item);
            var details=T("arsenal.rarity."+(int)RarityOf(item))+" · "+T("arsenal.level")+" "+_arsenal.Level(item);
            if (_guidedPurchase != null) details = T(TextKeys.GuidedDesertPurchase) + "\n" + details;
            if(item is WeaponDefinition w)
            {
                float damage=0,range=0; string unit=T("arsenal.damage");
                switch(w.Attack) {
                    case BulletAttack a: damage=a.Damage*a.Pellets; range=a.Range; break;
                    case StreamAttack a: damage=a.DamagePerSecond;range=a.Range;unit=T("arsenal.dps");break;
                    case BeamAttack a:
                        damage = a.ChargeTime > 0f ? a.Damage : a.Damage / a.Interval;
                        range = a.Range;
                        if (a.ChargeTime <= 0f) unit = T("arsenal.dps");
                        break;
                    case ChainAttack a:damage=a.Damage;range=a.Range;break;
                    case ExplosiveAttack a:damage=a.DirectDamage+a.ExplosionDamage;range=a.Range;break;
                    case MeleeAttack a:damage=a.Damage;range=a.Range;break;
                }
                details += "\n"+unit+": "+(damage*_arsenal.DamageFactor(w)).ToString("0.#")+"   "+T("arsenal.range")+": "+range.ToString("0.#")+
                    "\n"+T("arsenal.interval")+": "+w.Cooldown.ToString("0.##")+" s";
                if(w.UsesAmmo) details+="\n"+T("arsenal.magazine")+": "+UnityEngine.Mathf.CeilToInt(w.MagazineSize*(_arsenal.Has(w,WeaponUpgrade.Magazine)?1.5f:1f))+"   "+T("arsenal.reload")+": "+w.ReloadTime.ToString("0.#")+" s";
                if(w.TryGet<SilencerModule>(out _)) details+="\n◈ "+T("arsenal.silencer");
                if(w.Attack is BulletAttack bullet && bullet.Pierce>0) details+="   ◆ "+T("arsenal.pierce")+" +"+bullet.Pierce;
                if(w.Attack is ChainAttack chain) details+="   ↯ "+T("arsenal.chain")+" +"+chain.Jumps;
                if(!w.Upgradable) details+="\n"+T("arsenal.no_upgrades");
            }
            else if(item is ThrowableDefinition th) details+="\n"+T("arsenal.range")+": "+th.Range+"   "+T("arsenal.radius")+": "+(th.EffectRadius*_arsenal.RadiusFactor(th)).ToString("0.#")+"\n"+T("arsenal.stack")+": "+(th.MaxStack+_arsenal.StackBonus(th));
            var unique=item is WeaponDefinition uw && uw.UniqueReward;
            View.Card(Name(item),details,open?T("arsenal.open"):unique?T("arsenal.reward"):T("arsenal.locked"),
                open?T("arsenal.open"):unique?T("arsenal.reward"):T("arsenal.buy")+" · "+ArsenalService.Price(item),
                "★ "+T(mark==PoolMark.Favorite?"arsenal.normal":"arsenal.favorite"),"⊘ "+T(mark==PoolMark.Excluded?"arsenal.normal":"arsenal.exclude"),_arsenal.CanBuy(item),open);
            ShowUpgrades(item);
            ClearPreview(); _selection=new CancellationTokenSource(); LoadPreview(item,_selection.Token).Forget();
        }
        private async UniTask LoadPreview(ContentDefinition item,CancellationToken token)
        {
            var reference=item is WeaponDefinition w?_shared.WeaponVisuals?.Find(w)?.HeldPrefab:_shared.ItemVisuals?.Find(item)?.Flying;
            if(reference==null || !reference.RuntimeKeyIsValid()) return;
            IsLoading=true;
            var owner=_assets.CreateOwner("Arsenal preview: "+item.Id); _owner=owner;
            try { var prefab=await owner.LoadAsync<GameObject>(reference,token); token.ThrowIfCancellationRequested(); if(IsOpen) View.Preview.Show(prefab, item is ThrowableDefinition); }
            catch(OperationCanceledException) { }
            catch(Exception e) { GameLog.Exception(LogChannel.Visual,e,"Arsenal preview"); }
            finally { if(!token.IsCancellationRequested) IsLoading=false; }
        }
        private void ClearPreview() { IsLoading=false; _selection?.Cancel(); _selection?.Dispose(); _selection=null; if(View!=null)View.Preview.Release(); _owner?.Dispose(); _owner=null; }
        private void ShowUpgrades(ContentDefinition item)
        {
            int level=_arsenal.Level(item), price=_arsenal.UpgradePrice(item);
            bool forkNext=item is WeaponDefinition && (level+1==4 || level+1==8);
            var labels=new string[4];var available=new bool[4];
            if(item is WeaponDefinition w && w.Upgradable)
                for(int i=0;i<4;i++)
                {
                    int gate=i<2?4:8,choice=i%2;var fork=w.Fork(gate);var kind=choice==0?fork.First:fork.Second;
                    bool selected=_arsenal.ForkChoice(w,gate)==choice;
                    int cost=level<gate?w.UpgradeCost(gate):Math.Max(1,(int)Math.Ceiling(w.UpgradeCost(level)*0.1));
                    labels[i]=(selected?"★ ":"")+gate+": "+T("upgrade."+kind)+" · "+cost;
                    available[i]=_arsenal.IsOpen(item) && (level>=gate || level+1==gate) && !selected && fork.Available(choice) && cost>0 && _arsenal.Money>=cost;
                }
            View.Upgrades(price>0?T(forkNext?"upgrade.choose":"upgrade.buy")+" · "+price:T("upgrade.unavailable"),price>0 && !forkNext && _arsenal.Money>=price,labels,available);
        }
        private void Upgrade(){if(_arsenal.Upgrade(Item))UpgradeDone();}
        private void Fork(int level,int choice)
        {
            if(!(Item is WeaponDefinition w))return;
            bool ok=_arsenal.Level(w)+1==level?_arsenal.Upgrade(w,choice):_arsenal.ChangeFork(w,level,choice);
            if(ok)UpgradeDone();
        }
        private void UpgradeDone(){if(_audio.Catalog!=null)_audio.Play(_audio.Catalog.Purchase,SoundChannel.Interface);Fill();}
        private void Buy()
        {
            if (!_arsenal.Buy(Item)) return;
            if (_audio.Catalog != null) _audio.Play(_audio.Catalog.Purchase, SoundChannel.Interface);
            if (_guidedPurchase == null)
            {
                Fill();
                return;
            }

            Close();
            GuidedPurchaseCompleted?.Invoke();
        }
        private void Favorite() => Mark(PoolMark.Favorite);
        private void Exclude() => Mark(PoolMark.Excluded);
        private void Mark(PoolMark value) { var item=Item;if(item!=null && _arsenal.SetMark(item,_arsenal.Mark(item)==value?PoolMark.Normal:value))Fill(); }
        public void Dispose()
        {
            Close(); if(!_initialized)return;_initialized=false;_texts.LanguageChanged-=LanguageChanged;
            if(View==null)return; View.BackClicked-=Back;View.PreviousClicked-=Previous;View.NextClicked-=Next;View.Selected-=Select;
            View.UpgradeClicked-=Upgrade;View.ForkClicked-=Fork;
            View.BuyClicked-=Buy;View.FavoriteClicked-=Favorite;View.ExcludeClicked-=Exclude;
        }
    }
}
