

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
    private Rid _frameBufferRid;
    private Rid _vertexArrayRid;
    private Rid _indexArrayRid;
    private Rid _shaderRid;
    private Rid _pipelineRid;
    private Rid _samplerRid;
    private Rid _uniformBufferMatrixRid;
    private Rid _uniformSetRid;
    private Rid _indexBufferRid;
    private Rid _vertexBufferPosRid;
    private Rid _vertexBufferUvRid;
    private Texture2Drd _texture2DRd = new();
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

    [Export] public Vector2I Size { get; set { field = value; Update(); } } = new(256, 256);
    [Export]
    public Color ClearColor
    {
        get; set
        {
            field = value;
            Draw();
            EmitChanged();
        }
    } = Colors.Transparent;
    [Export]
    public Mesh Mesh
    {
        get; set
        {
            if (field != null && field.IsConnected(Resource.SignalName.Changed, new(this, nameof(Update))))
                field.Changed -= Update;
            field = value;
            Update();
            if (field != null) field.Changed += Update;
        }
    }
    [Export]
    public Texture2D Texture
    {
        get; set
        {
            field = value; ResetUniform(); Draw(); EmitChanged();
        }
    }
    [Export]
    public Projection Projection
    {
        get; set
        {
            field = value; ResetUniform(); Draw(); EmitChanged();
        }
    } = Projection.Identity;
    [Export]
    public RDShaderFile Glsl
    {
        get; set
        {
            if (field != null && field.IsConnected(Resource.SignalName.Changed, new(this, nameof(Update))))
                field.Changed -= Update;
            field = value;
            Update();
            if (field != null) field.Changed += Update;
        }
    } = GD.Load<RDShaderFile>("res://base_texture.glsl");

    private void Update()
    {
        Init(); ResetUniform(); Draw(); EmitChanged();
    }

    public MeshTextureRd()
    {
        _samplerRid = Rd.SamplerCreate(new());
    }

    protected override void Dispose(bool disposing)
    {
        Rd.CallDeferred(RD.MethodName.FreeRid, _samplerRid);
        base.Dispose(disposing);
    }

    public void Init()
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
        _shaderRid = Rd.ShaderCreateFromSpirV(shaderSpirv);

        var texFormat = new RDTextureFormat();
        var tex_view = new RDTextureView();
        texFormat.TextureType = RD.TextureType.Type2D;
        texFormat.Height = (uint)Size.Y;
        texFormat.Width = (uint)Size.X;
        texFormat.Format = RD.DataFormat.R8G8B8A8Unorm;
        texFormat.UsageBits = RD.TextureUsageBits.SamplingBit | RD.TextureUsageBits.ColorAttachmentBit;

        var frameBufferTextureRid = Rd.TextureCreate(texFormat, tex_view);

        var surfaceArray = Mesh.SurfaceGetArrays(0);
        var vertexArray = surfaceArray[(int)Mesh.ArrayType.Vertex];
        var indexArray = surfaceArray[(int)Mesh.ArrayType.Index];
        var uvArray = surfaceArray[(int)Mesh.ArrayType.TexUV];
        if (vertexArray.VariantType == Variant.Type.PackedVector2Array)
        {
            vertexArray = vertexArray.AsVector2Array().Select(v => new Vector3(v.X, v.Y, 0)).ToArray();
        }
        var points = vertexArray.AsVector3Array().SelectMany<Vector3, float>(v => { return [v.X, v.Y, v.Z]; }).ToArray();
        var points_bytes = points.CastSpan<float, byte>();

        var indices = indexArray.AsInt32Array();
        if (indices.Length > 0)
        {
            var indices_byte = indices.CastSpan<int, byte>();

            _indexBufferRid = Rd.IndexBufferCreate((uint)indices.Length, RD.IndexBufferFormat.Uint32, indices_byte.ToArray());

            _indexArrayRid = Rd.IndexArrayCreate(_indexBufferRid, 0, (uint)indices.Length);
        }

        var uvs = uvArray.AsVector2Array().SelectMany<Vector2, float>(v => [v.X, v.Y]).ToArray();
        var uv_bytes = uvs.CastSpan<float, byte>();

        _vertexBufferPosRid = Rd.VertexBufferCreate((uint)points_bytes.Length, points_bytes.ToArray());

        _vertexBufferUvRid = Rd.VertexBufferCreate((uint)uv_bytes.Length, uv_bytes.ToArray());

        var vertex_buffers = new Godot.Collections.Array<Rid> { _vertexBufferPosRid, _vertexBufferUvRid };

        var vertex_attrs = new Godot.Collections.Array<RDVertexAttribute> {
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

        var vertexFormat = Rd.VertexFormatCreate(vertex_attrs);

        _vertexArrayRid = Rd.VertexArrayCreate((uint)(points.Length / 3), vertexFormat, vertex_buffers);

        var blend = new RDPipelineColorBlendState();
        blend.Attachments.Add(new RDPipelineColorBlendStateAttachment());

        _frameBufferRid = Rd.FramebufferCreate([frameBufferTextureRid]);

        _pipelineRid = Rd.RenderPipelineCreate(
                   _shaderRid,
                   Rd.FramebufferGetFormat(_frameBufferRid),
                   vertexFormat,
                   RD.RenderPrimitive.Triangles,
                   new RDPipelineRasterizationState() { CullMode = RD.PolygonCullMode.Front },
                   new RDPipelineMultisampleState(),
                   new RDPipelineDepthStencilState(),
                   blend
               );
        _texture2DRd.TextureRdRid = frameBufferTextureRid;
    }

    private void ResetUniform()
    {
        if (Texture == null || !Texture.GetRid().IsValid || !_shaderRid.IsValid)
        {
            return;
        }
        var matrixArray = new float[] {
            Projection[0][0], Projection[0][1], Projection[0][2], Projection[0][3],
            Projection[1][0], Projection[1][1], Projection[1][2], Projection[1][3],
            Projection[2][0], Projection[2][1], Projection[2][2], Projection[2][3],
            Projection[3][0], Projection[3][1], Projection[3][2], Projection[3][3]
        };
        _uniformBufferMatrixRid = Rd.UniformBufferCreate(4 * 4 * 4, matrixArray.CastSpan<float, byte>().ToArray());

        _uniformMatrix.ClearIds();
        _uniformMatrix.AddId(_uniformBufferMatrixRid);

        _uniformTex.ClearIds();
        _uniformTex.AddId(_samplerRid);
        _uniformTex.AddId(RS.TextureGetRdTexture(Texture.GetRid()));

        _uniformSetRid = Rd.UniformSetCreate([_uniformMatrix, _uniformTex], _shaderRid, 0);
    }

    public void Draw()
    {
        if (!(_pipelineRid.IsValid &&
        _frameBufferRid.IsValid &&
        _texture2DRd.TextureRdRid.IsValid &&
        _vertexArrayRid.IsValid &&
        _uniformSetRid.IsValid))
        {
            return;
        }
        var drawList = Rd.DrawListBegin(_frameBufferRid, RD.DrawFlags.ClearColorAll, [ClearColor]);
        Rd.DrawListBindRenderPipeline(drawList, _pipelineRid);
        Rd.DrawListBindVertexArray(drawList, _vertexArrayRid);
        Rd.DrawListBindUniformSet(drawList, _uniformSetRid, 0);
        if (_indexArrayRid.IsValid) Rd.DrawListBindIndexArray(drawList, _indexArrayRid);
        Rd.DrawListDraw(drawList, _indexArrayRid.IsValid, 1);
        Rd.DrawListEnd();
    }

    public override Rid _GetRid()
    {
        return _texture2DRd.GetRid();
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
        _samplerRid = Rd.SamplerCreate(new());
    }


}
static class Ext
{
    public static ReadOnlySpan<TTo> CastSpan<TFrom, TTo>(this TFrom[] source) where TTo : struct where TFrom : struct
    {
        return MemoryMarshal.Cast<TFrom, TTo>(source);
    }
}