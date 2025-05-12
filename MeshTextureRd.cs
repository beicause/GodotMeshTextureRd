

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using RD = Godot.RenderingDevice;
using RS = Godot.RenderingServer;


[GlobalClass, Tool]
public partial class MeshTextureRd : Texture2D, ISerializationListener
{
    private readonly RD Rd = RS.GetRenderingDevice();
    private Rid FrameBufferTextureRid
    {
        get; set
        {
            RS.TextureReplace(TextureRd, value.IsValid ? RS.TextureRdCreate(value) : RS.Texture2DPlaceholderCreate());
            if (field.IsValid) { Rd.FreeRid(field); }
            field = value;
        }
    }
    private Rid FrameBufferRid;
    private Rid VertexArrayRid;
    private Rid IndexArrayRid;
    private Rid ShaderRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid PipelineRid;
    private Rid SamplerRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid UniformSetRid;
    private Rid IndexBufferRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid VertexBufferPosRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid VertexBufferUvRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private readonly Rid TextureRd = RS.Texture2DPlaceholderCreate();
    private readonly RDUniform _uniformTex = new()
    {
        UniformType = RD.UniformType.SamplerWithTexture,
        Binding = 0
    };

    private readonly Godot.Collections.Array<RDVertexAttribute> _vertexAttrs = [
            new() {
                Format = RD.DataFormat.R32G32B32Sfloat,
                Location = 0,
                Stride = 4 * 3
            },
            new(){
                Format = RD.DataFormat.R32G32Sfloat,
                Location= 1,
                Stride = 4 * 2
        }];

    private readonly long _vertexFormat;
    private bool _is2DMesh = false;

    private bool _shaderDirty = false;
    private bool _pipelineDirty = false;
    private bool _meshDirty = false;
    private bool _uniformSetDirty = false;
    [Export] public Vector2I Size { get; set { field = value; QueueUpdatePipeline(); } } = new(256, 256);
    [Export]
    public Color BgColor { get; set { field = value; QueueUpdate(); } } = Colors.Transparent;
    [Export]
    public Mesh Mesh
    {
        get; set
        {
            if (field != null) field.Changed -= QueueUpdateMesh;
            field = value;
            QueueUpdateMesh();
            if (field != null) field.Changed += QueueUpdateMesh;
        }
    }
    [Export]
    public Texture2D BaseTexture
    {
        get; set
        {
            if (field != null) field.Changed -= QueueUpdateUniformSet;
            field = value;
            QueueUpdateUniformSet();
            if (field != null) field.Changed += QueueUpdateUniformSet;
        }
    }
    [Export]
    public Projection Projection { get; set { field = value; QueueUpdate(); } } = Projection.Identity;
    [Export]
    public RDShaderFile GlslFile
    {
        get; set
        {
            if (field != null) field.Changed -= QueueUpdateShader;
            field = value;
            QueueUpdateShader();
            if (field != null) field.Changed += QueueUpdateShader;
        }
    } = GD.Load<RDShaderFile>("res://glsl/base_texture.glsl");
    [Export]
    public bool GenerateMipmaps
    {
        get; set
        {
            field = value;
            QueueUpdatePipeline();
        }
    } = false;

    private bool updateQueued = false;
    private RDTextureFormat texFormat = new();
    private RDTextureView texView = new();

    public void Update()
    {
        if (_meshDirty)
        {
            ResetVertex();
            _meshDirty = false;
        }
        if (_shaderDirty)
        {
            ResetShader();
            ResetPipeline();
            ResetUniform();
            _shaderDirty = false;
        }
        if (_pipelineDirty)
        {
            ResetPipeline();
            _pipelineDirty = false;
        }
        if (_uniformSetDirty)
        {
            ResetUniform();
            _uniformSetDirty = false;
        }
        DrawList();
        EmitChanged();
        updateQueued = false;
    }
    private void QueueUpdate()
    {
        if (updateQueued)
        {
            return;
        }
        updateQueued = true;
        CallDeferred(nameof(Update));
    }
    private void QueueUpdateShader()
    {
        _shaderDirty = true; QueueUpdate();
    }

    private void QueueUpdatePipeline()
    {
        _pipelineDirty = true; QueueUpdate();
    }

    private void QueueUpdateUniformSet()
    {
        _uniformSetDirty = true; QueueUpdate();
    }

    private void QueueUpdateMesh()
    {
        _meshDirty = true; QueueUpdate();
    }

    public MeshTextureRd()
    {
        SamplerRid = Rd.SamplerCreate(new());
        _vertexFormat = Rd.VertexFormatCreate(_vertexAttrs);
        if (GlslFile != null)
        {
            GlslFile.Changed += QueueUpdateShader;
        }
    }

    public void Destroy()
    {
        FrameBufferTextureRid = new();
        FrameBufferRid = new();
        VertexArrayRid = new();
        IndexArrayRid = new();
        ShaderRid = new();
        PipelineRid = new();
        SamplerRid = new();
        UniformSetRid = new();
        IndexBufferRid = new();
        VertexBufferPosRid = new();
        VertexBufferUvRid = new();
    }

    protected override void Dispose(bool disposing)
    {
        // Console.WriteLine("disposing, rd is " + (IsInstanceValid(Rd) ? "valid" : "null"));
        // Suppress errors when exiting.
        if (IsInstanceValid(Rd))
        {
            Destroy();
            RS.FreeRid(TextureRd);
        }
        base.Dispose(disposing);
    }

    private void ResetVertex()
    {
        if (Mesh == null ||
            Mesh.GetSurfaceCount() == 0)
        {
            return;
        }
        var surfaceArray = Mesh.SurfaceGetArrays(0);
        var vertexArray = surfaceArray[(int)Mesh.ArrayType.Vertex];
        var indexArray = surfaceArray[(int)Mesh.ArrayType.Index];
        var uvArray = surfaceArray[(int)Mesh.ArrayType.TexUV];
        _is2DMesh = false;
        if (vertexArray.VariantType == Variant.Type.PackedVector2Array)
        {
            _is2DMesh = true;
            // 2D Mesh needs to be downscaled to be visible in normal projection.
            vertexArray = vertexArray.AsVector2Array().Select(v => new Vector3(v.X, v.Y, 0) / 100).ToArray();
        }
        var vertexArray3 = vertexArray.AsVector3Array();
        var vertexCount = (uint)vertexArray3.Length;
        var vertexArrayBuffer = vertexArray3.SelectMany<Vector3, float>(v => { return [v.X, v.Y, v.Z]; }).ToArray();
        var vertexBytes = MemoryMarshal.Cast<float, byte>(vertexArrayBuffer);
        var isIndex16 = vertexCount <= 0xffff;
        var indices = indexArray.AsInt32Array();
        var indices16 = isIndex16 ? indices.Select(i => (short)i).ToArray() : [];
        if (indices.Length > 0)
        {
            var indicesBytes = isIndex16 ? MemoryMarshal.Cast<short, byte>(indices16) : MemoryMarshal.Cast<int, byte>(indices);
            IndexBufferRid = Rd.IndexBufferCreate((uint)indices.Length, isIndex16 ? RD.IndexBufferFormat.Uint16 : RD.IndexBufferFormat.Uint32, indicesBytes.ToArray());
            IndexArrayRid = Rd.IndexArrayCreate(IndexBufferRid, 0, (uint)indices.Length);
        }
        var uvs = uvArray.AsVector2Array().SelectMany<Vector2, float>(v => [v.X, v.Y]).ToArray();
        var uvBytes = MemoryMarshal.Cast<float, byte>(uvs);

        VertexBufferPosRid = Rd.VertexBufferCreate((uint)vertexBytes.Length, vertexBytes.ToArray());
        VertexBufferUvRid = Rd.VertexBufferCreate((uint)uvBytes.Length, uvBytes.ToArray());
        var vertexBuffers = new Godot.Collections.Array<Rid> { VertexBufferPosRid, VertexBufferUvRid };
        VertexArrayRid = Rd.VertexArrayCreate(vertexCount, _vertexFormat, vertexBuffers);
    }

    private void ResetShader()
    {
        var shaderSpirv = GlslFile.GetSpirV();
        ShaderRid = Rd.ShaderCreateFromSpirV(shaderSpirv);
    }

    private RDPipelineRasterizationState pipelineRasterizationState = new() { FrontFace = RD.PolygonFrontFace.CounterClockwise, CullMode = RD.PolygonCullMode.Back };
    private RDPipelineMultisampleState pipelineMultisampleState = new();
    private RDPipelineDepthStencilState pipelineDepthStencilState = new();

    private void ResetPipeline()
    {
        if (GlslFile == null)
        {
            return;
        }
        texFormat.TextureType = RD.TextureType.Type2D;
        texFormat.Width = (uint)Size.X;
        texFormat.Height = (uint)Size.Y;
        texFormat.Format = RD.DataFormat.R8G8B8A8Unorm;
        texFormat.Mipmaps = GenerateMipmaps ? GetImageRequiredMipmaps(texFormat.Width, texFormat.Height, 0) : 1; ;
        texFormat.UsageBits = RD.TextureUsageBits.SamplingBit | RD.TextureUsageBits.ColorAttachmentBit | RD.TextureUsageBits.CanCopyToBit | RD.TextureUsageBits.CanCopyFromBit;

        FrameBufferTextureRid = Rd.TextureCreate(texFormat, texView);

        var blend = new RDPipelineColorBlendState();
        blend.Attachments.Add(new RDPipelineColorBlendStateAttachment());

        FrameBufferRid = Rd.FramebufferCreate([FrameBufferTextureRid]);

        pipelineRasterizationState.CullMode = _is2DMesh ? RD.PolygonCullMode.Front : RD.PolygonCullMode.Back;

        PipelineRid = Rd.RenderPipelineCreate(
                   ShaderRid,
                   Rd.FramebufferGetFormat(FrameBufferRid),
                   _vertexFormat,
                   RD.RenderPrimitive.Triangles,
                    pipelineRasterizationState,
                    pipelineMultisampleState,
                    pipelineDepthStencilState,
                    blend
               );
    }

    private void ResetUniform()
    {
        if (!SamplerRid.IsValid || BaseTexture == null || !BaseTexture.GetRid().IsValid || !ShaderRid.IsValid)
        {
            return;
        }
        _uniformTex.ClearIds();
        _uniformTex.AddId(SamplerRid);
        _uniformTex.AddId(RS.TextureGetRdTexture(BaseTexture.GetRid()));

        UniformSetRid = UniformSetCacheRD.GetCache(ShaderRid, 0, [_uniformTex]);
    }

    private void DrawList()
    {
        if (!(PipelineRid.IsValid &&
        FrameBufferRid.IsValid &&
        VertexArrayRid.IsValid &&
        UniformSetRid.IsValid))
        {
            return;
        }
        // If using Transform3D instead of Projection:
        // ReadOnlySpan<float> xformArray = [
        //     Transform[0,0], Transform[0,1], Transform[0,2],0,
        //     Transform[1,0], Transform[1,1], Transform[1,2],0,
        //     Transform[2,0], Transform[2,1], Transform[2,2],0,
        //     Transform[3,0], Transform[3,1], Transform[3,2],1,
        // ];
        ReadOnlySpan<float> xformArray = [
            Projection[0,0], Projection[0,1], Projection[0,2], Projection[0,3],
            Projection[1,0], Projection[1,1], Projection[1,2], Projection[1,3],
            Projection[2,0], Projection[2,1], Projection[2,2], Projection[2,3],
            Projection[3,0], Projection[3,1], Projection[3,2], Projection[3,3]
        ];
        var xformBytes = MemoryMarshal.Cast<float, byte>(xformArray);

        var drawList = Rd.DrawListBegin(FrameBufferRid, RD.DrawFlags.ClearColorAll, [BgColor]);
        Rd.DrawListBindRenderPipeline(drawList, PipelineRid);
        Rd.DrawListBindVertexArray(drawList, VertexArrayRid);
        Rd.DrawListBindUniformSet(drawList, UniformSetRid, 0);
        if (IndexArrayRid.IsValid) Rd.DrawListBindIndexArray(drawList, IndexArrayRid);
        Rd.DrawListSetPushConstant(drawList, xformBytes, (uint)xformBytes.Length);
        Rd.DrawListDraw(drawList, IndexArrayRid.IsValid, 1);
        Rd.DrawListEnd();
        if (GenerateMipmaps) CreateMipmaps();
    }

    private static uint GetImageRequiredMipmaps(uint p_width, uint p_height, uint p_depth)
    {
        uint w = p_width;
        uint h = p_height;
        uint d = p_depth;
        uint mipmaps = 1;
        while (true)
        {
            if (w == 1 && h == 1 && d == 1) break;
            w = Math.Max(1u, w >> 1);
            h = Math.Max(1u, h >> 1);
            d = Math.Max(1u, d >> 1);
            mipmaps++;
        }
        return mipmaps;
    }

    private void CreateMipmaps()
    {
        if (!FrameBufferTextureRid.IsValid)
        {
            return;
        }
        var img = Image.CreateFromData(Size.X, Size.Y, true, Image.Format.Rgba8, Rd.TextureGetData(FrameBufferTextureRid, 0));
        img.GenerateMipmaps();
        ReadOnlySpan<byte> data = img.GetData();
        var fmt = Rd.TextureGetFormat(FrameBufferTextureRid);
        var mipmapCount = fmt.Mipmaps;
        fmt.Mipmaps = 1;
        for (int i = 1; i < mipmapCount; i++)
        {
            var mipmapSize = new Vector2I(Math.Max(1, Size.X / (1 << i)), Math.Max(1, Size.Y / (1 << i)));
            fmt.Width = (uint)mipmapSize.X;
            fmt.Height = (uint)mipmapSize.Y;
            var start = (int)img.GetMipmapOffset(i);
            var d = data.Slice(start, mipmapSize.X * mipmapSize.Y * 4);
            var tex = Rd.TextureCreate(fmt, texView, [d.ToArray()]);

            Error err = Rd.TextureCopy(tex, FrameBufferTextureRid, Vector3.Zero, Vector3.Zero, new Vector3(mipmapSize.X, mipmapSize.Y, 0), 0, (uint)i, 0, 0);
            if (err != Error.Ok)
            {
                GD.PushError("Failed to generate mipmaps: " + err.ToString());
                return;
            }
        }
    }

    public override Rid _GetRid()
    {
        return TextureRd;
    }

    public override int _GetWidth()
    {
        return Size.X;
    }

    public override int _GetHeight()
    {
        return Size.Y;
    }

    public void OnBeforeSerialize()
    {
        Destroy();
        RS.FreeRid(TextureRd);
    }

    public void OnAfterDeserialize()
    {
        SamplerRid = Rd.SamplerCreate(new());
    }
}
