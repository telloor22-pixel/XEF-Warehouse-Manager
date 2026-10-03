using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(XEFWarehouseManager.Mod), "XEF Warehouse Manager", "0.1.1", "XEF / OpenAI")]

namespace XEFWarehouseManager
{
    public sealed class Mod : MelonMod
    {
        const string PC="Project.Code.Gameplay.Controllers.ProductsController";
        const string OC="Project.Code.Gameplay.Controllers.OrderController";
        const string OCT="Project.Code.Gameplay.Services.OrderCatalogType";
        const string IA="Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1";
        bool open, onlyUnlocked=true, countPending=true;
        int target=30,page;
        string search="",status="F7 öffnet/schließt den Warehouse Manager.";
        DateTime next=DateTime.MinValue;
        Type? pcT,ocT,octT,inputT,keyT,objT,rectT,glT,optT;
        object? pc,oc,cfg;
        readonly List<Row> rows=new();
        readonly Dictionary<int,int> targets=new();
        sealed class Row { public int Def,Pick,Stock,Pending,Inside; public float Price; public bool Unlocked; public string Name=""; }

        public override void OnInitializeMelon(){ LoggerInstance.Msg("XEF Warehouse Manager loaded. Hotkey F7"); ResolveUnity(); ResolveGame(); }
        public override void OnUpdate(){
            if(F7()){ open=!open; Cursor(open); if(open) Refresh(true); }
            if(open && DateTime.UtcNow>=next) Refresh(false);
        }
        public override void OnGUI(){
            if(!open)return;
            try{
                ResolveUnity(); if(glT==null||rectT==null)return;
                var rect=Activator.CreateInstance(rectT,new object[]{40f,40f,1180f,820f})!;
                GL("BeginArea",rect); GL("BeginVertical",Opts());
                GL("Label","XEF Warehouse Manager v0.1.1   [F7]",Opts());
                GL("BeginHorizontal",Opts());
                GL("Label","Suche:",Opts(W(55))); search=Convert.ToString(GL("TextField",search,Opts(W(260))))??"";
                onlyUnlocked=Convert.ToBoolean(GL("Toggle",onlyUnlocked,"nur freigeschaltet",Opts(W(155)))??onlyUnlocked);
                countPending=Convert.ToBoolean(GL("Toggle",countPending,"Unterwegs berücksichtigen",Opts(W(200)))??countPending);
                GL("Label","Standard-Soll:",Opts(W(100)));
                var s=Convert.ToString(GL("TextField",target.ToString(),Opts(W(65))))??target.ToString();
                if(int.TryParse(s,out var n))target=Math.Max(0,Math.Min(9999,n));
                if(B("Aktualisieren",115))Refresh(true); GL("EndHorizontal");
                GL("Space",6f); GL("BeginHorizontal",Opts());
                if(B("ALLE BIS SOLL AUFFÜLLEN",270))OrderAll();
                if(B("Soll für alle = Standard",210)){foreach(var r in rows)targets[r.Def]=target;status=$"Sollbestand für {rows.Count} Artikel auf {target} gesetzt.";}
                GL("FlexibleSpace");GL("Label",$"Artikel: {rows.Count}",Opts(W(100)));GL("EndHorizontal");
                GL("Label",status,Opts()); GL("Space",4f);
                GL("BeginHorizontal",Opts()); foreach(var h in new[]{("Artikel",315f),("Bestand",75f),("Unterwegs",80f),("Box",55f),("Preis",75f),("Soll",65f)})GL("Label",h.Item1,Opts(W(h.Item2))); GL("Label","Aktionen",Opts()); GL("EndHorizontal");
                var f=Filter().ToList(); int max=Math.Max(0,(f.Count-1)/18); page=Math.Min(page,max);
                foreach(var r in f.Skip(page*18).Take(18)) Draw(r);
                GL("FlexibleSpace");GL("BeginHorizontal",Opts());
                if(B("< Seite",90))page=Math.Max(0,page-1);GL("Label",$"Seite {page+1}/{max+1}",Opts(W(120)));if(B("Seite >",90))page=Math.Min(max,page+1);
                GL("FlexibleSpace");GL("Label","Normales Bestellsystem / normales Geld",Opts());GL("EndHorizontal");
                GL("EndVertical");GL("EndArea");
            }catch(Exception e){LoggerInstance.Warning("GUI: "+e.GetBaseException().Message);}
        }
        IEnumerable<Row> Filter(){
            IEnumerable<Row> q=rows;if(onlyUnlocked)q=q.Where(r=>r.Unlocked);
            if(!string.IsNullOrWhiteSpace(search)){var z=search.Trim();q=q.Where(r=>r.Name.IndexOf(z,StringComparison.OrdinalIgnoreCase)>=0||r.Def.ToString().Contains(z));}
            return q.OrderBy(r=>r.Stock+(countPending?r.Pending*Math.Max(1,r.Inside):0)).ThenBy(r=>r.Name);
        }
        void Draw(Row r){
            if(!targets.TryGetValue(r.Def,out var t))targets[r.Def]=t=target;
            int pu=countPending?r.Pending*Math.Max(1,r.Inside):0, miss=Math.Max(0,t-r.Stock-pu), boxes=miss<=0?0:(int)Math.Ceiling(miss/(double)Math.Max(1,r.Inside));
            GL("BeginHorizontal",Opts());GL("Label",r.Name,Opts(W(315)));GL("Label",r.Stock.ToString(),Opts(W(75)));GL("Label",r.Pending.ToString(),Opts(W(80)));GL("Label",r.Inside.ToString(),Opts(W(55)));GL("Label",r.Price.ToString("0.00"),Opts(W(75)));
            var x=Convert.ToString(GL("TextField",t.ToString(),Opts(W(65))))??t.ToString();if(int.TryParse(x,out var v))targets[r.Def]=Math.Max(0,Math.Min(9999,v));
            if(B("+1 Box",78))Order(r,1);if(B("+5",52))Order(r,5);if(B(boxes>0?$"Auffüllen ({boxes})":"Voll",125)&&boxes>0)Order(r,boxes);GL("EndHorizontal");
        }
        void OrderAll(){
            Refresh(true);var ids=new List<int>();float cost=0;
            foreach(var r in Filter()){if(r.Pick<0)continue;if(!targets.TryGetValue(r.Def,out var t))t=target;int pu=countPending?r.Pending*Math.Max(1,r.Inside):0,miss=Math.Max(0,t-r.Stock-pu),b=miss<=0?0:(int)Math.Ceiling(miss/(double)Math.Max(1,r.Inside));for(int i=0;i<b;i++)ids.Add(r.Pick);cost+=b*r.Price;}
            if(ids.Count==0){status="Alles bereits auf/über Soll.";return;} if(Submit(ids))status=$"Sammelbestellung: {ids.Count} Boxen, ca. {cost:0.00}.";
        }
        void Order(Row r,int b){if(b<=0||r.Pick<0)return;var ids=Enumerable.Repeat(r.Pick,b).ToList();if(Submit(ids))status=$"Bestellung: {b}x {r.Name} (ca. {b*r.Price:0.00}).";}
        bool Submit(List<int> ids){
            try{
                Ensure();if(oc==null)throw new Exception("OrderController nicht gefunden. Spielstand laden.");RaiseLimit(ids.Count);
                var a=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>x.GetName().Name=="Il2CppInterop.Runtime")??throw new Exception("Il2CppInterop.Runtime fehlt.");
                var at=a.GetType(IA,true)!.MakeGenericType(typeof(int));object arr;
                var lc=at.GetConstructor(new[]{typeof(long)});var ic=at.GetConstructor(new[]{typeof(int)});
                if(lc!=null)arr=lc.Invoke(new object[]{(long)ids.Count});else if(ic!=null)arr=ic.Invoke(new object[]{ids.Count});else throw new Exception("Array ctor fehlt.");
                var item=at.GetProperty("Item")??throw new Exception("Array Item fehlt.");for(int i=0;i<ids.Count;i++)item.SetValue(arr,ids[i],new object[]{i});
                var m=ocT!.GetMethods(BindingFlags.Public|BindingFlags.Instance).FirstOrDefault(x=>x.Name=="RequestOrderAsync"&&x.GetParameters().Length==3)??throw new Exception("RequestOrderAsync fehlt.");
                m.Invoke(oc,new[]{arr,Enum.ToObject(octT!,0),(object)true});next=DateTime.UtcNow.AddSeconds(1);return true;
            }catch(Exception e){status="Bestellung fehlgeschlagen: "+e.GetBaseException().Message;LoggerInstance.Error(e);return false;}
        }
        void Refresh(bool verbose){
            next=DateTime.UtcNow.AddSeconds(1);
            try{
                Ensure();if(pc==null){if(verbose)status="ProductsController nicht verfügbar. Spielstand laden.";return;}
                cfg=Get(pc,"_productsConfig","ProductsConfig");
                if(cfg==null&&oc!=null){var ps=Get(oc,"_productsService");if(ps!=null)cfg=Get(ps,"ProductsConfig","_config");}
                if(cfg==null)throw new Exception("ProductsConfig nicht gefunden.");
                var pend=Pending();var rebuilt=new List<Row>();var products=Get(pc,"Products","_products");
                foreach(var info in Each(products)){
                    if(info==null)continue;int d=I(Get(info,"DefinitionId"),-1);if(d<0)continue;int st=I(Get(info,"AllCount"),0);bool un=Bo(Call(pc,"IsProductBought",d),true);
                    var pd=Call(cfg,"FindProductDefinition",d);if(pd==null)continue;string name=Convert.ToString(Get(pd,"Name","FullName","HoldingTextKey","Title"))??$"Artikel #{d}";int inside=Math.Max(1,I(Get(pd,"InsideCount"),1));
                    var pick=Call(cfg,"FindPickupDefinitionByProductId",d);int pid=pick==null?-1:I(Get(pick,"Id"),-1);float price=0;if(pick!=null){var pv=Call(pick,"GetPrice")??Get(pick,"Price","Cost","PurchasePrice");if(pv!=null)price=Convert.ToSingle(pv,CultureInfo.InvariantCulture);}
                    pend.TryGetValue(pid,out var p);rebuilt.Add(new Row{Def=d,Pick=pid,Name=name,Stock=st,Pending=p,Inside=inside,Price=price,Unlocked=un});if(!targets.ContainsKey(d))targets[d]=target;
                }
                rows.Clear();rows.AddRange(rebuilt.GroupBy(r=>r.Def).Select(g=>g.First()));if(verbose)status=$"Bestand aktualisiert: {rows.Count} Artikel.";
            }catch(Exception e){if(verbose)status="Aktualisierung fehlgeschlagen: "+e.GetBaseException().Message;LoggerInstance.Warning(e.ToString());}
        }
        Dictionary<int,int> Pending(){var d=new Dictionary<int,int>();if(oc==null)return d;foreach(var s in Each(Get(oc,"_orderSaveDatas","OrderSaveDatas"))){if(s==null)continue;int id=I(Get(s,"Id","PickupId"),-1),c=Math.Max(1,I(Get(s,"Count"),1));if(id>=0)d[id]=d.TryGetValue(id,out var o)?o+c:c;}return d;}
        void RaiseLimit(int n){if(cfg==null)return;var o=Get(cfg,"OrderingPrice","_orderingPrice");if(o==null)return;int cur=I(Get(o,"OrderLimit","_orderLimit"),0);if(cur<n)Set(o,n,"_orderLimit","OrderLimit");}
        void Ensure(){ResolveGame();ResolveUnity();if(objT==null)return;if(pc==null)pc=Find(pcT);if(oc==null)oc=Find(ocT);}
        object? Find(Type? t){if(t==null||objT==null)return null;var m=objT.GetMethod("FindObjectOfType",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(Type)},null);return m?.Invoke(null,new object[]{t});}
        void ResolveGame(){if(pcT!=null&&ocT!=null&&octT!=null)return;var a=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>x.GetName().Name=="Il2CppProject");if(a==null)return;Type[] ts;try{ts=a.GetTypes();}catch(ReflectionTypeLoadException e){ts=e.Types.Where(x=>x!=null).Cast<Type>().ToArray();}pcT=a.GetType(PC,false)??ts.FirstOrDefault(t=>t.Name=="ProductsController");ocT=a.GetType(OC,false)??ts.FirstOrDefault(t=>t.Name=="OrderController");octT=a.GetType(OCT,false)??ts.FirstOrDefault(t=>t.Name=="OrderCatalogType");if(pcT!=null)LoggerInstance.Msg("ProductsController: "+pcT.FullName);if(ocT!=null)LoggerInstance.Msg("OrderController: "+ocT.FullName);if(octT!=null)LoggerInstance.Msg("OrderCatalogType: "+octT.FullName);}
        void ResolveUnity(){inputT??=Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule");keyT??=Type.GetType("UnityEngine.KeyCode, UnityEngine.CoreModule");objT??=Type.GetType("UnityEngine.Object, UnityEngine.CoreModule");rectT??=Type.GetType("UnityEngine.Rect, UnityEngine.CoreModule");glT??=Type.GetType("UnityEngine.GUILayout, UnityEngine.IMGUIModule");optT??=Type.GetType("UnityEngine.GUILayoutOption, UnityEngine.IMGUIModule");}
        bool F7(){try{ResolveUnity();if(inputT==null||keyT==null)return false;var m=inputT.GetMethod("GetKeyDown",BindingFlags.Public|BindingFlags.Static,null,new[]{keyT},null);return m!=null&&(bool)(m.Invoke(null,new[]{Enum.Parse(keyT,"F7")})??false);}catch{return false;}}
        void Cursor(bool v){try{var t=Type.GetType("UnityEngine.Cursor, UnityEngine.CoreModule");if(t==null)return;t.GetProperty("visible",BindingFlags.Public|BindingFlags.Static)?.SetValue(null,v);var p=t.GetProperty("lockState",BindingFlags.Public|BindingFlags.Static);if(p!=null&&p.PropertyType.IsEnum){var state=Enum.Parse(p.PropertyType,v?"None":"Locked");p.SetValue(null,state);}}catch(Exception e){LoggerInstance.Warning("Cursor: "+e.Message);}}
        object? GL(string name,params object?[] args){if(glT==null)return null;foreach(var m in glT.GetMethods(BindingFlags.Public|BindingFlags.Static).Where(x=>x.Name==name&&x.GetParameters().Length==args.Length)){try{return m.Invoke(null,args);}catch{}}return null;}
        Array Opts(params object?[] x){if(optT==null)return Array.Empty<object>();var a=Array.CreateInstance(optT,x.Length);for(int i=0;i<x.Length;i++)if(x[i]!=null)a.SetValue(x[i],i);return a;}
        object? W(float v)=>GL("Width",v);bool B(string s,float w)=>GL("Button",s,Opts(W(w))) is bool b&&b;
        static object? Get(object? o,params string[] n){if(o==null)return null;var t=o.GetType();var f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;foreach(var x in n){try{var p=t.GetProperty(x,f);if(p!=null&&p.GetIndexParameters().Length==0)return p.GetValue(o);var q=t.GetField(x,f);if(q!=null)return q.GetValue(o);}catch{}}return null;}
        static bool Set(object o,object v,params string[] n){var t=o.GetType();var f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;foreach(var x in n){try{var p=t.GetProperty(x,f);if(p!=null&&p.CanWrite){p.SetValue(o,Convert.ChangeType(v,p.PropertyType));return true;}var q=t.GetField(x,f);if(q!=null){q.SetValue(o,Convert.ChangeType(v,q.FieldType));return true;}}catch{}}return false;}
        static object? Call(object? o,string n,params object?[] a){if(o==null)return null;var f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;foreach(var m in o.GetType().GetMethods(f).Where(x=>x.Name==n&&x.GetParameters().Length==a.Length)){try{return m.Invoke(o,a);}catch{}}return null;}
        static IEnumerable<object?> Each(object? c){if(c is IEnumerable e){foreach(var x in e)yield return x;}}
        static int I(object? x,int d){try{return x==null?d:Convert.ToInt32(x);}catch{return d;}} static bool Bo(object? x,bool d){try{return x==null?d:Convert.ToBoolean(x);}catch{return d;}}
    }
}