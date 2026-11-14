using Purge.Presentation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Purge.Editor.Dev
{
    internal static partial class RuntimeScenesBuilder
    {
        private static ArsenalScreen BuildArsenal(Transform parent)
        {
            var screen=Screen<ArsenalScreen>(parent,"ArsenalScreen",new Color(0.025f,0.025f,0.03f,1f));
            var area=SafeArea(screen.transform);var icons=UiIcons();
            var back=RoundButton(area,"Back",icons.ChevronLeft,TopLeft,new Vector2(100,-90),84,36);
            SetReference(screen,"_back",back);
            var title=Text(area,"Class","",38,true,TopLeft,Center,new Vector2(520,-90),new Vector2(600,65),TextAlignmentOptions.Center);
            SetReference(screen,"_className",title);
            var prev=TextButton(area,"Previous","‹",false,null,64,36);Place(prev,TopLeft,new Vector2(200,-90),new Vector2(70,64));
            var next=TextButton(area,"Next","›",false,null,64,36);Place(next,TopLeft,new Vector2(840,-90),new Vector2(70,64));
            SetReference(screen,"_previous",prev);SetReference(screen,"_next",next);
            var money=Text(area,"Money","0",38,true,new Vector2(1,1),Center,new Vector2(-170,-90),new Vector2(240,65),TextAlignmentOptions.Center);
            SetReference(screen,"_money",money);
            var left=PanelArea(area,"List",Vector2.zero,new Vector2(0,1),new Vector2(48,40),new Vector2(920,-165));
            var content=ScrollColumn(left);var grid=TileGrid(content,"Items");
            var layout=grid.GetComponent<GridLayoutGroup>();layout.constraintCount=3;layout.cellSize=new Vector2(264,158);layout.spacing=new Vector2(12,12);
            var tile=TextButton(grid,"Template","",false,null,158,20);
            var label=tile.transform.Find("Label").GetComponent<RectTransform>();label.anchorMin=new Vector2(0,0);label.anchorMax=new Vector2(1,0);label.pivot=new Vector2(0.5f,0);label.anchoredPosition=new Vector2(0,8);label.sizeDelta=new Vector2(-12,54);
            var text=label.GetComponent<TMP_Text>();text.characterSpacing=0;text.textWrappingMode=TextWrappingModes.Normal;
            Icon(tile.transform,"Icon",icons.Ranged,Center,new Vector2(0,30),80);
            tile.gameObject.SetActive(false);SetReference(screen,"_grid",grid);SetReference(screen,"_template",tile);
            var card=PanelArea(area,"Card",new Vector2(1,0),new Vector2(1,1),new Vector2(-850,40),new Vector2(-48,-165));
            var name=Text(card,"Name","",38,true,new Vector2(0.5f,1),new Vector2(0.5f,1),new Vector2(0,-15),new Vector2(750,60),TextAlignmentOptions.Center);AutoSize(name);SetReference(screen,"_title",name);
            var imageRect=Rect(card,"Preview",new Vector2(0.5f,1),new Vector2(0,-260),new Vector2(640,350));
            var image=imageRect.gameObject.AddComponent<RawImage>();image.color=Color.white;
            var preview=imageRect.gameObject.AddComponent<ArsenalPreview>();
            var stage=new GameObject("ArsenalPreviewStage");stage.transform.position=new Vector3(0,-1500,0);
            var pivot=new GameObject("Pivot").transform;pivot.SetParent(stage.transform,false);
            var cameraObject=new GameObject("Camera");cameraObject.transform.SetParent(stage.transform,false);cameraObject.transform.localPosition=new Vector3(0,0,-5);
            var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.035f,0.035f,0.04f);camera.nearClipPlane=0.01f;camera.farClipPlane=20;
            var lightObject=new GameObject("Light");lightObject.transform.SetParent(stage.transform,false);lightObject.transform.localPosition=new Vector3(-2,3,-3);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Point;light.range=15;light.intensity=3;light.shadows=LightShadows.None;
            cameraObject.SetActive(false);SetReference(preview,"_camera",camera);SetReference(preview,"_pivot",pivot);SetReference(preview,"_image",image);SetReference(screen,"_preview",preview);
            var details=Text(card,"Details","",26,false,new Vector2(0.5f,1),new Vector2(0.5f,1),new Vector2(0,-450),new Vector2(740,180),TextAlignmentOptions.TopLeft);SetReference(screen,"_details",details);
            var status=Text(card,"Status","",23,false,new Vector2(0.5f,0),new Vector2(0.5f,0),new Vector2(0,185),new Vector2(740,50),TextAlignmentOptions.Center);SetReference(screen,"_status",status);
            var buy=TextButton(card,"Buy","",true,null,68,26);Place(buy,new Vector2(0.5f,0),new Vector2(0,138),new Vector2(680,68));SetReference(screen,"_buy",buy);
            var favorite=TextButton(card,"Favorite","",false,null,68,22);Place(favorite,new Vector2(0.5f,0),new Vector2(-176,50),new Vector2(328,68));SetReference(screen,"_favorite",favorite);
            var exclude=TextButton(card,"Exclude","",false,null,68,22);Place(exclude,new Vector2(0.5f,0),new Vector2(176,50),new Vector2(328,68));SetReference(screen,"_exclude",exclude);
            return screen;
        }
    }
}
