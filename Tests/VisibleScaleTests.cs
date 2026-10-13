using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

internal static class VisibleScaleTests
{
    internal static readonly Rect SideRect = new Rect(0f, 0f, 684.3f, 400f);
    internal static readonly Rect CloseRect = new Rect(-342.15f, -200f, 684.3f, 400f);
    internal static SurfaceProjection Side(float rightDepth = 0.02f)
    {
        var slope = (rightDepth - 1.014f) / SideRect.width;
        var originalSlope = (0.02f - 1.014f) / SideRect.width;
        var worldSlope = (84456f * 0.02f - 1000f * 1.014f) / SideRect.width - 1720f * originalSlope;
        return new SurfaceProjection(new Vector3(worldSlope + 1720f * slope, 0f, 1014f),
            new Vector3(720f * slope, 2f, 720f * 1.014f), new Vector3(slope, 0f, 1.014f), 0.01f, 3440f, 1440f);
    }
    internal static SurfaceProjection Close(bool crossing)
    {
        var slope = crossing ? 0.00015f : 0f; var depth = crossing ? 0.04f : 0.05f;
        return new SurfaceProjection(new Vector3(2f + 1720f * slope, 0f, 1720f * depth),
            new Vector3(720f * slope, 2f, 720f * depth), new Vector3(slope, 0f, depth), 0.01f, 3440f, 1440f);
    }
    internal static Vector3 Screen(SurfaceProjection p, Vector2 point)
    {
        var v = p.Project(point); return new Vector3(v.x, v.y, p.Depth(point));
    }
    internal static float Measure(ref ScaleMeasurement state, SurfaceProjection p, Rect rect, float last = -1f)
        => state.Measure(Screen(p, SurfaceProjection.Corner(rect, 0)), Screen(p, SurfaceProjection.Corner(rect, 1)), p, rect, last);

    // Independent oracle: intersect every pair of the nine canvas/frustum boundary lines,
    // then solve their inequalities on the selected horizontal line in double precision.
    internal static double Expected(SurfaceProjection p, Rect r)
    {
        var lines = new (double a, double b, double c)[] {
            (1,0,-r.xMin),(-1,0,r.xMax),(0,1,-r.yMin),(0,-1,r.yMax),
            (p.A.x,p.A.y,p.A.z),(p.Width*p.C.x-p.A.x,p.Width*p.C.y-p.A.y,p.Width*p.C.z-p.A.z),
            (p.B.x,p.B.y,p.B.z),(p.Height*p.C.x-p.B.x,p.Height*p.C.y-p.B.y,p.Height*p.C.z-p.B.z),
            (p.CameraDepth.x,p.CameraDepth.y,p.CameraDepth.z-p.Near)
        };
        bool Inside(double x, double y)
        {
            foreach (var l in lines) if (l.a*x+l.b*y+l.c < -1e-6) return false;
            return true;
        }
        bool Chord(double y, out double lo, out double hi)
        {
            lo=r.xMin; hi=r.xMax;
            foreach(var l in lines)
            {
                var c=l.b*y+l.c;
                if(l.a==0) { if(c < 0) return false; }
                else if(l.a>0) lo=Math.Max(lo,-c/l.a);
                else hi=Math.Min(hi,-c/l.a);
            }
            return hi>lo;
        }
        var y=(double)r.yMin;
        if(!Chord(y,out var left,out var right))
        {
            var ys = new List<double>();
            for(var i=0;i<lines.Length;i++) for(var j=i+1;j<lines.Length;j++)
            {
                var a=lines[i]; var b=lines[j]; var d=a.a*b.b-a.b*b.a;
                if(Math.Abs(d)<1e-15) continue;
                var x=(a.b*b.c-a.c*b.b)/d; var vy=(a.c*b.a-a.a*b.c)/d;
                if(Inside(x,vy)) ys.Add(vy);
            }
            if(ys.Count==0) return -1;
            ys.Sort(); y=(ys[0]+ys[ys.Count-1])*0.5;
            if(!Chord(y,out left,out right)) return -1;
        }
        (double x,double y) Project(double x) {
            var w=p.C.x*x+p.C.y*y+p.C.z;
            return ((p.A.x*x+p.A.y*y+p.A.z)/w,(p.B.x*x+p.B.y*y+p.B.z)/w);
        }
        var v0=Project(left);var v1=Project(right);
        return Math.Sqrt(Math.Pow(v1.x-v0.x,2)+Math.Pow(v1.y-v0.y,2))/(right-left);
    }
    internal static void Run(TestRun run)
    {
        var state=new ScaleMeasurement();var side=Side();
        var legacy=new ScaleMeasurement();var old=legacy.Measure(Screen(side,new Vector2(0,0)),Screen(side,new Vector2(SideRect.width,0)),side.Near,SideRect.width);
        var scale=Measure(ref state,side,SideRect);var expected=Expected(side,SideRect);
        run.Check("visible scale: logged side geometry", Math.Abs(old-83456f/684.3f)<0.1f && Math.Abs(scale-expected)<expected*1e-5,
            $"legacy {old:F6}, visible {scale:F6}, independent {expected:F6} px/unit");
        run.Check("visible scale: side uses clipped canvas length",state.Sample==ScaleMeasurement.Segment.ClippedBottom && state.CanvasWidth>0f && state.CanvasWidth<SideRect.width && state.Pixels<=3440f,"bottom segment inside four side planes");
        var full=new SurfaceProjection(new Vector3(2,0,500),new Vector3(0,2,300),new Vector3(0,0,1),0.01f,3440,1440);
        var a=Screen(full,new Vector2(0,0));var b=Screen(full,new Vector2(SideRect.width,0));
        var exact=legacy.Measure(a,b,full.Near,SideRect.width);
        run.Check("visible scale: fully visible is bit exact",BitConverter.SingleToInt32Bits(Measure(ref state,full,SideRect))==BitConverter.SingleToInt32Bits(exact),"unchanged endpoint distance and divisor");
        var smallNear=new SurfaceProjection(new Vector3(2,0,1),new Vector3(0,2,1),new Vector3(0,0,0.005f),0.001f,3440,1440);
        var smallRect=new Rect(0,0,1,1);
        var originalNear=legacy.Measure(Screen(smallNear,new Vector2(0,0)),Screen(smallNear,new Vector2(1,0)),Math.Max(0.01f,smallNear.Near),1f);
        run.Check("visible scale: fully visible preserves legacy near threshold",Measure(ref state,smallNear,smallRect)==originalNear,"bit-exact legacy fallback when the camera near plane is below 0.01");
        foreach(var crossing in new[]{false,true})
        {
            var p=Close(crossing); var cornersOutside=true;
            for(var i=0;i<4;i++) {var s=Screen(p,SurfaceProjection.Corner(CloseRect,i));cornersOutside &= s.z<=p.Near || s.x<0 || s.x>p.Width || s.y<0 || s.y>p.Height;}
            var value=Measure(ref state,p,CloseRect,2f);var want=Expected(p,CloseRect);
            run.Check("visible scale: close centred"+(crossing?" crossing near":" in front"),cornersOutside && state.Sample==ScaleMeasurement.Segment.Chord && state.CanvasWidth<CloseRect.width && Math.Abs(value-want)<want*1e-5,
                $"{value:F6} vs independent {want:F6}; chord {state.CanvasWidth:F4} canvas units");
        }
        var ortho=new SurfaceProjection(new Vector3(2,0,1720),new Vector3(0,2,720),new Vector3(0,0,1),0.01f,3440,1440,new Vector3(0,0.001f,0.02f));
        var orthoValue=Measure(ref state,ortho,CloseRect,2f);
        run.Check("visible scale: orthographic uses camera depth",state.Sample==ScaleMeasurement.Segment.Chord && Math.Abs(orthoValue-Expected(ortho,CloseRect))<1e-5,"near clipping independent of homogeneous divisor");
        var through=new SurfaceProjection(new Vector3(2,1.72f,0),new Vector3(0,0.72f,20),new Vector3(0,0.001f,0),0.01f,3440,1440);
        var throughRect=new Rect(-200,-200,400,400);var throughValue=Measure(ref state,through,throughRect,2f);
        run.Check("visible scale: bottom behind camera uses upper chord",state.Sample==ScaleMeasurement.Segment.Chord && Math.Abs(throughValue-Expected(through,throughRect))<0.001,"visible polygon crosses camera and near planes");
        var away=new SurfaceProjection(new Vector3(1,0,10000),new Vector3(0,1,10000),new Vector3(0,0,1),0.01f,3440,1440);
        var retained=Measure(ref state,away,SideRect,7.2f);
        run.Check("visible scale: invisible retains scale and bucket",retained==7.2f && state.Bucket(retained,17)==17,"no rebuild on leaving frustum");
        var unknown=Measure(ref state,away,SideRect);
        run.Check("visible scale: invisible initial view retains unbuilt bucket",unknown<0 && state.Bucket(unknown,int.MinValue)==int.MinValue,"no first build caused by visibility polling");
        run.Check("visible scale: offscreen corner compression ignored",!SurfaceProjection.Drifted(side,Side(0.04f),SideRect,1.5f),"near offscreen corner changes; visible contraction stays within margin");
        run.Check("visible scale: wholly invisible ignores extent drift",!SurfaceProjection.Drifted(full,away,SideRect,1.5f),"no visible band needs rebuilding");
        var triggers=0;
        foreach(var p in new[]{side,Close(false),Close(true),through})
        {
            var rect=p.C.y!=0 ? throughRect : p.C.z==1.014f ? SideRect : CloseRect;
            var first=Measure(ref state,p,rect);var bucket=state.Bucket(first,int.MinValue);
            for(var i=0;i<10000;i++) {var next=Measure(ref state,p,rect,first);if(state.Bucket(next,bucket)!=bucket || SurfaceProjection.Drifted(p,p,rect,1.5f)) triggers++;}
        }
        run.Check("visible scale: still camera zero triggers",triggers==0,$"{triggers} scale/extent triggers across 40000 frames");
        void Frame(){Measure(ref state,side,SideRect);Measure(ref state,Close(true),CloseRect);SurfaceProjection.Drifted(side,side,SideRect,1.5f);}
        for(var i=0;i<100;i++)Frame();var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<10000;i++)Frame();var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        run.Check("visible scale: zero per frame allocations",allocated==0,$"{allocated} bytes / 10000 frames");
        state.Measure(new Vector3(float.NaN,0,1),new Vector3(10,0,1),default,SideRect,2f);
        run.Check("visible scale: invalid projection preserves scalar rejection",state.Reason==ScaleMeasurement.Status.Span,"non-finite data retains legacy fallback");
        var source=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"../../../../VectorGraphic.cs"));
        run.Check("visible scale: measurement and bucket wired",source.Contains("_scaleMeasurement.Measure(a, b, projection, rect, _lastKnownScale)") && source.Contains("_scaleMeasurement.Bucket(scale, _builtForBucket)"),"runtime uses tested decisions");
    }
}
