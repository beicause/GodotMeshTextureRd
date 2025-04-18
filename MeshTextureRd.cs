

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
    private Rid UniformDataBufferRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid UniformSetRid;
    private Rid IndexBufferRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid VertexBufferPosRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private Rid VertexBufferUvRid { get; set { if (field.IsValid) Rd.FreeRid(field); field = value; } }
    private readonly Rid TextureRd = RS.Texture2DPlaceholderCreate();
    private readonly RDUniform _uniformData = new()
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

    private bool _shaderDirty = false;
    private bool _pipelineDirty = false;
    private bool _meshDirty = false;
    private bool _uniformSetDirty = false;
    [Export] public Vector2I Size { get; set { field = value; QueueUpdatePipeline(); } } = new(256, 256);
    [Export]
    public Color ClearColor { get; set { field = value; QueueUpdate(); } } = Colors.Transparent;
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
    public Projection Projection { get; set { field = value; QueueUpdateUniformSet(); } } = Projection.Identity;
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

    private bool updateQueued = false;

    public void Update()
    {
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
        if (_meshDirty)
        {
            ResetVertex();
            _meshDirty = false;
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
        UniformDataBufferRid = new();
        UniformSetRid = new();
        IndexBufferRid = new();
        VertexBufferPosRid = new();
        VertexBufferUvRid = new();
    }

    protected override void Dispose(bool disposing)
    {
        // Console.WriteLine("disposing, rd is " + (IsInstanceValid(Rd) ? "valid" : "null"));
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

    private void ResetShader()
    {
        var shaderSpirv = GlslFile.GetSpirV();
        ShaderRid = Rd.ShaderCreateFromSpirV(shaderSpirv);
    }

    private void ResetPipeline()
    {
        if (GlslFile == null)
        {
            return;
        }
        var texFormat = new RDTextureFormat();
        var texView = new RDTextureView();
        texFormat.TextureType = RD.TextureType.Type2D;
        texFormat.Width = (uint)Size.X;
        texFormat.Height = (uint)Size.Y;
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
        if (!SamplerRid.IsValid || BaseTexture == null || !BaseTexture.GetRid().IsValid || !ShaderRid.IsValid)
        {
            return;
        }
        var dataArray = new float[] {
            Projection[0][0], Projection[0][1], Projection[0][2], Projection[0][3],
            Projection[1][0], Projection[1][1], Projection[1][2], Projection[1][3],
            Projection[2][0], Projection[2][1], Projection[2][2], Projection[2][3],
            Projection[3][0], Projection[3][1], Projection[3][2], Projection[3][3]
        };
        var bytes = dataArray.CastSpan<float, byte>().ToArray();
        if (!UniformDataBufferRid.IsValid)
        {
            UniformDataBufferRid = Rd.UniformBufferCreate((uint)bytes.Length, bytes);
            _uniformData.ClearIds();
            _uniformData.AddId(UniformDataBufferRid);
        }
        else Rd.BufferUpdate(UniformDataBufferRid, 0, (uint)bytes.Length, bytes);

        _uniformTex.ClearIds();
        _uniformTex.AddId(SamplerRid);
        _uniformTex.AddId(RS.TextureGetRdTexture(BaseTexture.GetRid()));

        UniformSetRid = UniformSetCacheRD.GetCache(ShaderRid, 0, [_uniformData, _uniformTex]);
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

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
        UniformDataBufferRid = new();
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
