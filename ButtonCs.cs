using System;
using Godot;

public partial class ButtonCs : Button
{
    public override void _Pressed()
    {
        var cs = GetNode("../CS");
        for (int i = 0; i < 3; i++)
        {
            var sp = cs.GetChild<Sprite2D>(0);
            cs.RemoveChild(sp);
            (sp.Texture as MeshTextureRd).Destroy();
            RenderingServer.FreeRid(sp.Texture.GetRid());
            sp.Free();
        }
    }
}
