

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using RD = Godot.RenderingDevice;
using RS = Godot.RenderingServer;


[GlobalClass]
[Tool]
public partial class MeshTextureRd : Texture2D, ISerializationListener
{
    public static readonly RD Rd = RS.GetRenderingDevice();
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
    private Rid UniformBufferMatrixRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid UniformSetRid;
    private Rid IndexBufferRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid VertexBufferPosRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid VertexBufferUvRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private readonly Rid TextureRd = RS.Texture2DPlaceholderCreate();
    private RDUniform _uniformMatrix = new()
    {
        UniformType = RD.UniformType.UniformBuffer,
        Binding = 0
    };
    private RDUniform _uniformTex = new()
    {
        UniformType = RD.UniformType.SamplerWithTexture,
        Binding = 1
    };

    private bool _pipelineDirty = false;
    private bool _uniformSetDirty = false;
    [Export] public Vector2I Size { get; set { field = value; QueueUpdatePipeline(); } } = new(256, 256);
    [Export]
    public Color ClearColor { get; set { field = value; Update(); } } = Colors.Transparent;
    [Export]
    public Mesh Mesh
    {
        get; set
        {
            if (field != null) field.Changed -= QueueUpdatePipeline;
            field = value;
            QueueUpdatePipeline();
            if (field != null) field.Changed += QueueUpdatePipeline;
        }
    }
    [Export]
    public Texture2D Texture
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
    public Projection Projection { get; set { field = value; _uniformSetDirty = true; CallDeferred(nameof(Update)); } } = Projection.Identity;
    [Export]
    public RDShaderFile Glsl
    {
        get; set
        {
            if (field != null) field.Changed -= QueueUpdatePipeline;
            field = value;
            QueueUpdatePipeline();
            if (field != null) field.Changed += QueueUpdatePipeline;
        }
    } = GD.Load<RDShaderFile>("res://glsl/base_texture.glsl");

    private void Update()
    {
        if (_pipelineDirty)
        {
            InitPipeline();
            ResetUniform();
            Draw();
            EmitChanged();
            _pipelineDirty = false;
            _uniformSetDirty = false;
        }
        else if (_uniformSetDirty)
        {
            ResetUniform();
            Draw();
            EmitChanged();
            _uniformSetDirty = false;
        }
        else
        {
            Draw();
            EmitChanged();
        }
    }

    private void QueueUpdatePipeline()
    {
        _pipelineDirty = true;
        CallDeferred(nameof(Update));
    }

    private void QueueUpdateUniformSet()
    {
        _uniformSetDirty = true;
        CallDeferred(nameof(Update));
    }

    public MeshTextureRd()
    {
        SamplerRid = Rd.SamplerCreate(new());
        Glsl.Changed += QueueUpdatePipeline;
    }

    public void Clean()
    {
        FrameBufferTextureRid = new();
        FrameBufferRid = new();
        VertexArrayRid = new();
        IndexArrayRid = new();
        ShaderRid = new();
        PipelineRid = new();
        SamplerRid = new();
        UniformBufferMatrixRid = new();
        UniformSetRid = new();
        IndexBufferRid = new();
        VertexBufferPosRid = new();
        VertexBufferUvRid = new();
    }

    protected override void Dispose(bool disposing)
    {
        Clean();
        RS.FreeRid(TextureRd);
        base.Dispose(disposing);
    }

    public void InitPipeline()
    {
        if (Mesh == null ||
            Glsl == null ||
            Mesh.GetSurfaceCount() == 0 ||
            Texture == null ||
            !Texture.GetRid().IsValid)
        {
            return;
        }
        var shaderSpirv = Glsl.GetSpirV();
        ShaderRid = Rd.ShaderCreateFromSpirV(shaderSpirv);

        var texFormat = new RDTextureFormat();
        var texView = new RDTextureView();
        texFormat.TextureType = RD.TextureType.Type2D;
        texFormat.Height = (uint)Size.Y;
        texFormat.Width = (uint)Size.X;
        texFormat.Format = RD.DataFormat.R8G8B8A8Unorm;
        texFormat.UsageBits = RD.TextureUsageBits.SamplingBit | RD.TextureUsageBits.ColorAttachmentBit;

        FrameBufferTextureRid = Rd.TextureCreate(texFormat, texView);

        var surfaceArray = Mesh.SurfaceGetArrays(0);
        var vertexArray = surfaceArray[(int)Mesh.ArrayType.Vertex];
        var indexArray = surfaceArray[(int)Mesh.ArrayType.Index];
        var uvArray = surfaceArray[(int)Mesh.ArrayType.TexUV];
        if (vertexArray.VariantType == Variant.Type.PackedVector2Array)
        {
            vertexArray = vertexArray.AsVector2Array().Select(v => new Vector3(v.X, v.Y, 0)).ToArray();
        }
        var points = vertexArray.AsVector3Array().SelectMany<Vector3, float>(v => { return [v.X, v.Y, v.Z]; }).ToArray();
        var pointsBytes = points.CastSpan<float, byte>();

        var indices = indexArray.AsInt32Array();
        if (indices.Length > 0)
        {
            var indices_byte = indices.CastSpan<int, byte>();

            IndexBufferRid = Rd.IndexBufferCreate((uint)indices.Length, RD.IndexBufferFormat.Uint32, indices_byte.ToArray());

            IndexArrayRid = Rd.IndexArrayCreate(IndexBufferRid, 0, (uint)indices.Length);
        }

        var uvs = uvArray.AsVector2Array().SelectMany<Vector2, float>(v => [v.X, v.Y]).ToArray();
        var uvBytes = uvs.CastSpan<float, byte>();

        VertexBufferPosRid = Rd.VertexBufferCreate((uint)pointsBytes.Length, pointsBytes.ToArray());

        VertexBufferUvRid = Rd.VertexBufferCreate((uint)uvBytes.Length, uvBytes.ToArray());

        var vertexBuffers = new Godot.Collections.Array<Rid> { VertexBufferPosRid, VertexBufferUvRid };

        var vertexAttrs = new Godot.Collections.Array<RDVertexAttribute> {
            new() {
                Format = RD.DataFormat.R32G32B32Sfloat,
                Location = 0,
                Stride = 4 * 3
            },
            new(){
                Format = RD.DataFormat.R32G32Sfloat,
                Location= 1,
                Stride = 4 * 2
            }
        };

        var vertexFormat = Rd.VertexFormatCreate(vertexAttrs);

        VertexArrayRid = Rd.VertexArrayCreate((uint)(points.Length / 3), vertexFormat, vertexBuffers);

        var blend = new RDPipelineColorBlendState();
        blend.Attachments.Add(new RDPipelineColorBlendStateAttachment());

        FrameBufferRid = Rd.FramebufferCreate([FrameBufferTextureRid]);

        PipelineRid = Rd.RenderPipelineCreate(
                   ShaderRid,
                   Rd.FramebufferGetFormat(FrameBufferRid),
                   vertexFormat,
                   RD.RenderPrimitive.Triangles,
                   new RDPipelineRasterizationState() { CullMode = RD.PolygonCullMode.Front },
                   new RDPipelineMultisampleState(),
                   new RDPipelineDepthStencilState(),
                   blend
               );
    }

    private void ResetUniform()
    {
        if (Texture == null || !Texture.GetRid().IsValid || !ShaderRid.IsValid)
        {
            return;
        }
        var matrixArray = new float[] {
            Projection[0][0], Projection[0][1], Projection[0][2], Projection[0][3],
            Projection[1][0], Projection[1][1], Projection[1][2], Projection[1][3],
            Projection[2][0], Projection[2][1], Projection[2][2], Projection[2][3],
            Projection[3][0], Projection[3][1], Projection[3][2], Projection[3][3]
        };
        UniformBufferMatrixRid = Rd.UniformBufferCreate(4 * 4 * 4, matrixArray.CastSpan<float, byte>().ToArray());

        _uniformMatrix.ClearIds();
        _uniformMatrix.AddId(UniformBufferMatrixRid);

        _uniformTex.ClearIds();
        _uniformTex.AddId(SamplerRid);
        _uniformTex.AddId(RS.TextureGetRdTexture(Texture.GetRid()));

        UniformSetRid = Rd.UniformSetCreate([_uniformMatrix, _uniformTex], ShaderRid, 0);
    }

    public void Draw()
    {
        if (!(PipelineRid.IsValid &&
        FrameBufferRid.IsValid &&
        VertexArrayRid.IsValid &&
        UniformSetRid.IsValid))
        {
            return;
        }
        var drawList = Rd.DrawListBegin(FrameBufferRid, RD.DrawFlags.ClearColorAll, [ClearColor]);
        Rd.DrawListBindRenderPipeline(drawList, PipelineRid);
        Rd.DrawListBindVertexArray(drawList, VertexArrayRid);
        Rd.DrawListBindUniformSet(drawList, UniformSetRid, 0);
        if (IndexArrayRid.IsValid) Rd.DrawListBindIndexArray(drawList, IndexArrayRid);
        Rd.DrawListDraw(drawList, IndexArrayRid.IsValid, 1);
        Rd.DrawListEnd();
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

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        SamplerRid = Rd.SamplerCreate(new());
    }
}


static class Ext
{
    public static ReadOnlySpan<TTo> CastSpan<TFrom, TTo>(this TFrom[] source) where TTo : struct where TFrom : struct
    {
        return MemoryMarshal.Cast<TFrom, TTo>(source);
    }
}
