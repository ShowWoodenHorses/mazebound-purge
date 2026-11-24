using Purge.Presentation.UI;
using Purge.Presentation.UI.Shapes;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Purge.Editor.Dev
{
    internal static partial class RuntimeScenesBuilder
    {
        private static void BindArsenalCardFrame(ArsenalScreen screen, UiShape frame, UiStyle style)
        {
            frame.StrokeWidth = style.Stroke + 1f;
            SetReference(screen, "_cardFrame", frame);
            SetReference(screen, "_style", style);
        }

        private static ArsenalScreen BuildArsenal(Transform parent)
        {
            var screen=Screen<ArsenalScreen>(parent,"ArsenalScreen",new Color(0.025f,0.025f,0.03f,1f));
            var area=SafeArea(screen.transform);var icons=UiIcons();
            var back=RoundButton(area,"Back",icons.ChevronLeft,TopLeft,new Vector2(100,-90),84,36);
            SetReference(screen,"_back",back);
            var title=Text(area,"Class","",38,true,TopLeft,Center,new Vector2(520,-90),new Vector2(540,78),TextAlignmentOptions.Center);
            SetReference(screen,"_className",AutoSize(title));
            var prev=TextButton(area,"Previous","‹",false,null,64,36);Place(prev,TopLeft,new Vector2(200,-90),new Vector2(70,64));
            var next=TextButton(area,"Next","›",false,null,64,36);Place(next,TopLeft,new Vector2(840,-90),new Vector2(70,64));
            SetReference(screen,"_previous",prev);SetReference(screen,"_next",next);
            var money=Text(area,"Money","0",38,true,new Vector2(1,1),Center,new Vector2(-170,-90),new Vector2(240,65),TextAlignmentOptions.Center);
            AddBalanceCoin(money);
            SetReference(screen,"_money",money);
            var left=PanelArea(area,"List",Vector2.zero,new Vector2(0,1),new Vector2(48,40),new Vector2(920,-165));
            var content=ScrollColumn(left);var grid=TileGrid(content,"Items");
            var layout=grid.GetComponent<GridLayoutGroup>();layout.constraintCount=3;layout.cellSize=new Vector2(264,174);layout.spacing=new Vector2(12,12);
            var tile=TextButton(grid,"Template","",false,null,174,20);
            var label=tile.transform.Find("Label").GetComponent<RectTransform>();label.anchorMin=new Vector2(0,0);label.anchorMax=new Vector2(1,0);label.pivot=new Vector2(0.5f,0);label.anchoredPosition=new Vector2(0,8);label.sizeDelta=new Vector2(-16,69);
            var text=label.GetComponent<TMP_Text>();text.characterSpacing=0;text.textWrappingMode=TextWrappingModes.Normal;
            Icon(tile.transform,"Icon",icons.Ranged,Center,new Vector2(0,38),72);
            tile.gameObject.SetActive(false);SetReference(screen,"_grid",grid);SetReference(screen,"_template",tile);
            var card=PanelArea(area,"Card",new Vector2(1,0),new Vector2(1,1),new Vector2(-850,40),new Vector2(-48,-165));
            BindArsenalCardFrame(screen, card.GetComponent<UiShape>(), _style);
            var name=Text(card,"Name","",38,true,new Vector2(0.5f,1),new Vector2(0.5f,1),new Vector2(0,-15),new Vector2(750,78),TextAlignmentOptions.Center);AutoSize(name);SetReference(screen,"_title",name);
            var imageRect=Rect(card,"Preview",new Vector2(0.5f,1),new Vector2(0,-171),new Vector2(480,142));
            var image=imageRect.gameObject.AddComponent<RawImage>();image.color=Color.white;image.enabled=false;
            var preview=imageRect.gameObject.AddComponent<ArsenalPreview>();
            var stage=new GameObject("ArsenalPreviewStage");stage.transform.position=new Vector3(0,-1500,0);
            var pivot=new GameObject("Pivot").transform;pivot.SetParent(stage.transform,false);
            var cameraObject=new GameObject("Camera");cameraObject.transform.SetParent(stage.transform,false);cameraObject.transform.localPosition=new Vector3(0,0,-5);
            var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.035f,0.035f,0.04f);camera.nearClipPlane=0.01f;camera.farClipPlane=20;
            var lightObject=new GameObject("Light");lightObject.transform.SetParent(stage.transform,false);lightObject.transform.localPosition=new Vector3(-2,3,-3);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Point;light.range=15;light.intensity=3;light.shadows=LightShadows.None;
            cameraObject.SetActive(false);SetReference(preview,"_camera",camera);SetReference(preview,"_pivot",pivot);SetReference(preview,"_image",image);SetReference(screen,"_preview",preview);
            var details = Text(card, "Details", "", 26, false, Center, Center, Vector2.zero, Vector2.zero, TextAlignmentOptions.TopLeft);
            Stretch(details.rectTransform);
            details.rectTransform.offsetMin = new Vector2(30, 398);
            details.rectTransform.offsetMax = new Vector2(-30, -256);
            SetReference(screen, "_details", AutoSize(details));
            var status=Text(card,"Status","",23,false,new Vector2(0.5f,0),new Vector2(0.5f,0),new Vector2(0,169),new Vector2(740,48),TextAlignmentOptions.Center);SetReference(screen,"_status",AutoSize(status));
            var buy=TextButton(card,"Buy","",true,null,68,26);Place(buy,new Vector2(0.5f,0),new Vector2(0,132),new Vector2(680,68));SetReference(screen,"_buy",buy);
            var favorite=TextButton(card,"Favorite","",false,null,68,22);Place(favorite,new Vector2(0.5f,0),new Vector2(-176,50),new Vector2(328,68));SetReference(screen,"_favorite",favorite);
            var exclude=TextButton(card,"Exclude","",false,null,68,22);Place(exclude,new Vector2(0.5f,0),new Vector2(176,50),new Vector2(328,68));SetReference(screen,"_exclude",exclude);
            var upgrade=TextButton(card,"Upgrade","",true,null,56,23);Place(upgrade,new Vector2(0.5f,0),new Vector2(0,360),new Vector2(680,56));SetReference(screen,"_upgrade",upgrade);
            var fields=new[]{"_fork4a","_fork4b","_fork8a","_fork8b"};
            for(int i=0;i<4;i++){var button=TextButton(card,"Fork"+i,"",false,null,50,19);Place(button,new Vector2(0.5f,0),new Vector2(i%2==0?-176:176,i<2?300:245),new Vector2(328,50));SetReference(screen,fields[i],button);}
            return screen;
        }
    }
}
