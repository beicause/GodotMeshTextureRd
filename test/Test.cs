using Godot;
using System;

public partial class Test : Button
{
    public override void _Pressed()
    {
        var sp = GetNodeOrNull<Sprite2D>("../Sprite2D");
        if (sp != null)
        {
            GetParent().RemoveChild(sp);
            for (int i = 0; i < 5; i++)
            {
                sp.Texture.Duplicate().Dispose();
            }
            sp.Texture = null;
            sp.Free();
        }
    }
}
