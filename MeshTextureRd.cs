

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using RD = Godot.RenderingDevice;
using RS = Godot.RenderingServer;


[GlobalClass]
[Tool]
public partial class MeshTextureRd : Texture2D
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
    private readonly RDUniform _uniformMatrix = new()
    {
        UniformType = RD.UniformType.UniformBuffer,
        Binding = 0
    };
    private readonly RDUniform _uniformTex = new()
    {
        UniformType = RD.UniformType.SamplerWithTexture,
        Binding = 1
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

    private bool _pipelineDirty = false;
    private bool _meshDirty = false;
    private bool _uniformSetDirty = false;
    [Export] public Vector2I Size { get; set { field = value; QueueUpdatePipeline(); } } = new(256, 256);
    [Export]
    public Color ClearColor { get; set { field = value; CallDeferred(nameof(Update)); } } = Colors.Transparent;
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
    public Projection Projection { get; set { field = value; QueueUpdateUniformSet(); } } = Projection.Identity;
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
            ResetPipeline();
            _pipelineDirty = false;
        }
        if (_uniformSetDirty)
        {
            ResetUniform();
            _uniformSetDirty = false;
        }
        if (_meshDirty)
        {
            ResetVertex();
            _meshDirty = false;
        }
        Draw();
        EmitChanged();
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

    private void QueueUpdateMesh()
    {
        _meshDirty = true;
        CallDeferred(nameof(Update));
    }

    public MeshTextureRd()
    {
        SamplerRid = Rd.SamplerCreate(new());
        Glsl.Changed += QueueUpdatePipeline;
        _vertexFormat = Rd.VertexFormatCreate(_vertexAttrs);
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

    public void ResetVertex()
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
        if (vertexArray.VariantType == Variant.Type.PackedVector2Array)
        {
            vertexArray = vertexArray.AsVector2Array().Select(v => new Vector3(v.X, v.Y, 0)).ToArray();
        }
        var points = vertexArray.AsVector3Array().SelectMany<Vector3, float>(v => { return [v.X, v.Y, v.Z]; }).ToArray();
        var pointsBytes = points.CastSpan<float, byte>();
        var indices = indexArray.AsInt32Array();
        if (indices.Length > 0)
        {
            var indicesBytes = indices.CastSpan<int, byte>();
            IndexBufferRid = Rd.IndexBufferCreate((uint)indices.Length, RD.IndexBufferFormat.Uint32, indicesBytes.ToArray());
            IndexArrayRid = Rd.IndexArrayCreate(IndexBufferRid, 0, (uint)indices.Length);
        }
        var uvs = uvArray.AsVector2Array().SelectMany<Vector2, float>(v => [v.X, v.Y]).ToArray();
        var uvBytes = uvs.CastSpan<float, byte>();

        VertexBufferPosRid = Rd.VertexBufferCreate((uint)pointsBytes.Length, pointsBytes.ToArray());
        VertexBufferUvRid = Rd.VertexBufferCreate((uint)uvBytes.Length, uvBytes.ToArray());
        var vertexBuffers = new Godot.Collections.Array<Rid> { VertexBufferPosRid, VertexBufferUvRid };
        VertexArrayRid = Rd.VertexArrayCreate((uint)(points.Length / 3), _vertexFormat, vertexBuffers);
    }

    public void ResetPipeline()
    {
        if (Glsl == null)
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

        var blend = new RDPipelineColorBlendState();
        blend.Attachments.Add(new RDPipelineColorBlendStateAttachment());

        FrameBufferRid = Rd.FramebufferCreate([FrameBufferTextureRid]);

        PipelineRid = Rd.RenderPipelineCreate(
                   ShaderRid,
                   Rd.FramebufferGetFormat(FrameBufferRid),
                   _vertexFormat,
                   RD.RenderPrimitive.Triangles,
                   new RDPipelineRasterizationState() { CullMode = RD.PolygonCullMode.Front },
                   new RDPipelineMultisampleState(),
                   new RDPipelineDepthStencilState(),
                   blend
               );
    }

    private void ResetUniform()
    {
        if (!SamplerRid.IsValid || Texture == null || !Texture.GetRid().IsValid || !ShaderRid.IsValid)
        {
            return;
        }
        var matrixArray = new float[] {
            Projection[0][0], Projection[0][1], Projection[0][2], Projection[0][3],
            Projection[1][0], Projection[1][1], Projection[1][2], Projection[1][3],
            Projection[2][0], Projection[2][1], Projection[2][2], Projection[2][3],
            Projection[3][0], Projection[3][1], Projection[3][2], Projection[3][3]
        };
        var bytes = matrixArray.CastSpan<float, byte>().ToArray();
        if (!UniformBufferMatrixRid.IsValid)
        {
            UniformBufferMatrixRid = Rd.UniformBufferCreate((uint)bytes.Length, bytes);
            _uniformMatrix.ClearIds();
            _uniformMatrix.AddId(UniformBufferMatrixRid);
        }
        else Rd.BufferUpdate(UniformBufferMatrixRid, 0, (uint)bytes.Length, bytes);

        _uniformTex.ClearIds();
        _uniformTex.AddId(SamplerRid);
        _uniformTex.AddId(RS.TextureGetRdTexture(Texture.GetRid()));

        UniformSetRid = UniformSetCacheRD.GetCache(ShaderRid, 0, [_uniformMatrix, _uniformTex]);
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
}


static class Ext
{
    public static ReadOnlySpan<TTo> CastSpan<TFrom, TTo>(this TFrom[] source) where TTo : struct where TFrom : struct
    {
        return MemoryMarshal.Cast<TFrom, TTo>(source);
    }
}
