using Godot;
using System;

public partial class Test : Button
{
    public override void _Pressed()
    {
        var sp = GetNode<Sprite2D>("../Sprite2D");
        GetParent().RemoveChild(sp);
        sp.Free();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        GC.WaitForFullGCComplete();
        GC.WaitForPendingFinalizers();
    }
}
