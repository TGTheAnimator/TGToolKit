// TGToolKit — 3D Renderer (v3 — proper per-geometry texture binding)
// Architecture:
//   - Raw VertexBytes uploaded to GPU (no decompression)
//   - Dynamic InputLayout per geometry from VertexType flags
//   - Per-geometry texture binding via ShaderGroup.ShaderMapping
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using ToolKitV.Rendering;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using Format = SharpDX.DXGI.Format;

namespace ToolKitV.Models.Rendering
{
    // -------------------------------------------------------------------------
    // Constant buffer — 16-byte aligned
    // -------------------------------------------------------------------------
    [StructLayout(LayoutKind.Explicit, Size = 96)]
    struct SceneConstants
    {
        [FieldOffset(0)]  public Matrix  WorldViewProj;
        [FieldOffset(64)] public Vector3 LightDir;
        [FieldOffset(76)] public float   HasTexture;
        [FieldOffset(80)] public Vector4 Ambient;
    }

    // -------------------------------------------------------------------------
    // Per-geometry GPU data + the texture name this geometry uses
    // -------------------------------------------------------------------------
    class GeometryGpuData : IDisposable
    {
        public Buffer?      VertexBuffer;
        public Buffer?      IndexBuffer;
        public InputLayout? Layout;
        public int          VertexStride;
        public int          IndexCount;
        public string?      TextureName;  // resolved from ShaderGroup.ShaderMapping

        public void Dispose()
        {
            Layout?.Dispose();
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    public class Renderer : IDisposable
    {
        // DX resources
        private Device        _device;
        private DeviceContext _context;

        // Render target
        private Texture2D?         _rtTex;
        private RenderTargetView?  _rtv;
        private Texture2D?         _depthTex;
        private DepthStencilView?  _dsv;

        // WPF interop
        private DX11ImageSource _imageSource;
        public DX11ImageSource ImageSource => _imageSource;

        // Pipeline
        private VertexShader?   _vs;
        private PixelShader?    _ps;
        private Buffer          _cb;
        private SamplerState    _sampler;
        private RasterizerState _rs;
        private byte[]?         _vsBlob;

        // Texture cache: name → SRV (case-insensitive, matches YTD names)
        private readonly Dictionary<string, ShaderResourceView> _textures
            = new(StringComparer.OrdinalIgnoreCase);

        // Model
        private readonly List<GeometryGpuData> _geoms = new();

        // Camera
        public float CameraYaw      { get; set; } = MathF.PI * 0.25f;
        public float CameraPitch    { get; set; } = 0.35f;
        public float CameraDistance { get; set; } = 5.0f;
        public float PanX           { get; set; } = 0f;
        public float PanY           { get; set; } = 0f;
        private Vector3 _modelCenter = Vector3.Zero;
        private float   _modelRadius = 1.0f;

        // -------------------------------------------------------------------------
        public Renderer()
        {
            _imageSource = new DX11ImageSource();

            _device = new Device(
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 });
            _context = _device.ImmediateContext;

            InitPipeline();
        }

        private void InitPipeline()
        {
            _cb = new Buffer(_device, Utilities.SizeOf<SceneConstants>(),
                ResourceUsage.Default, BindFlags.ConstantBuffer,
                CpuAccessFlags.None, ResourceOptionFlags.None, 0);

            string hlslPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                           "Models", "Rendering", "DefaultShader.hlsl");
            string hlsl = File.ReadAllText(hlslPath);

            _vsBlob = SharpDX.D3DCompiler.ShaderBytecode.Compile(
                hlsl, "VS", "vs_5_0", SharpDX.D3DCompiler.ShaderFlags.None).Bytecode.Data;
            var psBlob = SharpDX.D3DCompiler.ShaderBytecode.Compile(
                hlsl, "PS", "ps_5_0", SharpDX.D3DCompiler.ShaderFlags.None).Bytecode.Data;

            _vs = new VertexShader(_device, _vsBlob);
            _ps = new PixelShader(_device, psBlob);

            _sampler = new SamplerState(_device, new SamplerStateDescription
            {
                Filter             = Filter.MinMagMipLinear,
                AddressU           = TextureAddressMode.Wrap,
                AddressV           = TextureAddressMode.Wrap,
                AddressW           = TextureAddressMode.Wrap,
                ComparisonFunction = Comparison.Never,
                MinimumLod         = 0,
                MaximumLod         = float.MaxValue
            });

            _rs = new RasterizerState(_device, new RasterizerStateDescription
            {
                FillMode             = FillMode.Solid,
                CullMode             = CullMode.None,
                IsDepthClipEnabled   = true,
                IsScissorEnabled     = false,
                IsMultisampleEnabled = false
            });
        }

        // -------------------------------------------------------------------------
        public void Resize(int w, int h)
        {
            if (w <= 0 || h <= 0) return;

            _rtv?.Dispose();
            _rtTex?.Dispose();
            _dsv?.Dispose();
            _depthTex?.Dispose();

            _rtTex = new Texture2D(_device, new Texture2DDescription
            {
                Width             = w,
                Height            = h,
                MipLevels         = 1,
                ArraySize         = 1,
                Format            = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags         = BindFlags.RenderTarget | BindFlags.ShaderResource,
                Usage             = ResourceUsage.Default,
                OptionFlags       = ResourceOptionFlags.Shared
            });

            _rtv = new RenderTargetView(_device, _rtTex);

            _depthTex = new Texture2D(_device, new Texture2DDescription
            {
                Width             = w,
                Height            = h,
                MipLevels         = 1,
                ArraySize         = 1,
                Format            = Format.D24_UNorm_S8_UInt,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags         = BindFlags.DepthStencil,
                Usage             = ResourceUsage.Default
            });
            _dsv = new DepthStencilView(_device, _depthTex);

            _imageSource.SetRenderTarget(_rtTex);
        }

        // -------------------------------------------------------------------------
        // Load drawable — extract per-geometry texture names from ShaderMapping
        // -------------------------------------------------------------------------
        public void LoadDrawable(DrawableBase drawable)
        {
            ClearGeometries();
            if (drawable == null) return;

            if (drawable is Drawable d)
            {
                _modelCenter   = d.BoundingCenter;
                _modelRadius   = Math.Max(0.5f, d.BoundingSphereRadius);
                CameraDistance = _modelRadius * 2.8f;
                PanX = 0f;
                PanY = 0f;
            }

            // Get the shader array so we can resolve texture names per geometry
            var shaderItems = drawable.ShaderGroup?.Shaders?.data_items;

            // Prioritise High LOD, fall back through others
            var models = drawable.DrawableModels?.High
                      ?? drawable.DrawableModels?.Med
                      ?? drawable.DrawableModels?.Low
                      ?? drawable.AllModels;

            if (models == null) return;

            foreach (var model in models)
            {
                if (model?.Geometries == null) continue;

                for (int gi = 0; gi < model.Geometries.Length; gi++)
                {
                    var geom = model.Geometries[gi];
                    if (geom == null) continue;

                    // Resolve this geometry's diffuse texture name via ShaderMapping
                    string? textureName = null;
                    if (shaderItems != null && model.ShaderMapping != null && gi < model.ShaderMapping.Length)
                    {
                        int si = model.ShaderMapping[gi];
                        if (si < shaderItems.Length)
                        {
                            var shader = shaderItems[si];
                            textureName = GetDiffuseTextureName(shader);
                        }
                    }

                    var vd = geom.VertexData;
                    if (vd?.VertexBytes == null || vd.VertexBytes.Length == 0) continue;
                    if (geom.IndexBuffer?.Indices == null) continue;

                    var declTypes    = vd.Info?.Types ?? VertexDeclarationTypes.GTAV1;
                    var layoutElements = GtaVertexLayout.GetLayoutForSimpleShader(vd.VertexType, declTypes);
                    if (layoutElements == null || layoutElements.Length == 0) continue;

                    InputLayout layout;
                    try { layout = new InputLayout(_device, _vsBlob, layoutElements); }
                    catch { continue; }

                    var vbData = vd.VertexBytes;
                    var vbDesc = new BufferDescription(
                        vbData.Length, ResourceUsage.Default,
                        BindFlags.VertexBuffer, CpuAccessFlags.None,
                        ResourceOptionFlags.None, 0);

                    Buffer vb;
                    try
                    {
                        using var s = new DataStream(vbData.Length, true, true);
                        s.Write(vbData, 0, vbData.Length);
                        s.Position = 0;
                        vb = new Buffer(_device, s, vbDesc);
                    }
                    catch { layout.Dispose(); continue; }

                    Buffer ib;
                    try { ib = Buffer.Create(_device, BindFlags.IndexBuffer, geom.IndexBuffer.Indices); }
                    catch { layout.Dispose(); vb.Dispose(); continue; }

                    _geoms.Add(new GeometryGpuData
                    {
                        VertexBuffer = vb,
                        IndexBuffer  = ib,
                        Layout       = layout,
                        VertexStride = vd.VertexStride,
                        IndexCount   = geom.IndexBuffer.Indices.Length,
                        TextureName  = textureName
                    });
                }
            }

            // Load textures embedded in the drawable's own ShaderGroup TXD
            TryLoadEmbeddedTextures(drawable);

            Render();
        }

        /// <summary>
        /// Returns the diffuse texture name from a shader's parameter list.
        /// GTA V shaders: first DataType==0 param is always the diffuse sampler.
        /// </summary>
        private static string? GetDiffuseTextureName(ShaderFX shader)
        {
            var plist = shader?.ParametersList;
            if (plist?.Parameters == null) return null;

            foreach (var p in plist.Parameters)
            {
                if (p.DataType == 0 && p.Data is Texture tex)
                    return tex.Name;
            }
            return null;
        }

        private void TryLoadEmbeddedTextures(DrawableBase drawable)
        {
            var txd = (drawable as Drawable)?.ShaderGroup?.TextureDictionary;
            if (txd?.Textures?.data_items == null) return;
            foreach (var tex in txd.Textures.data_items)
                LoadTextureToCacheOnly(tex);
        }

        // -------------------------------------------------------------------------
        // Public: load all textures from an external YTD into the cache.
        // Call this for every texture in the YTD — they'll be keyed by name and
        // matched per-geometry at render time via TextureName.
        // -------------------------------------------------------------------------
        public void LoadTexture(Texture cwTex)
        {
            LoadTextureToCacheOnly(cwTex);
            Render(); // update display immediately
        }

        private void LoadTextureToCacheOnly(Texture? cwTex)
        {
            if (cwTex?.Data?.FullData == null || cwTex.Width <= 0 || cwTex.Height <= 0) return;
            if (string.IsNullOrEmpty(cwTex.Name)) return;

            Format fmt = cwTex.Format switch
            {
                TextureFormat.D3DFMT_DXT1     => Format.BC1_UNorm,
                TextureFormat.D3DFMT_DXT3     => Format.BC2_UNorm,
                TextureFormat.D3DFMT_DXT5     => Format.BC3_UNorm,
                TextureFormat.D3DFMT_ATI1     => Format.BC4_UNorm,
                TextureFormat.D3DFMT_ATI2     => Format.BC5_UNorm,
                TextureFormat.D3DFMT_BC7      => Format.BC7_UNorm,
                TextureFormat.D3DFMT_A8R8G8B8 => Format.B8G8R8A8_UNorm,
                TextureFormat.D3DFMT_A8B8G8R8 => Format.R8G8B8A8_UNorm,
                _                             => Format.Unknown
            };
            if (fmt == Format.Unknown) return;

            bool isBC  = fmt >= Format.BC1_Typeless && fmt <= Format.BC7_UNorm_SRgb;
            var  bytes = cwTex.Data.FullData;
            int  levels = Math.Max(1, (int)cwTex.Levels);

            // Pin ONCE — must stay pinned until AFTER new Texture2D() returns
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr basePtr = pinned.AddrOfPinnedObject();
                var    rects   = new List<DataRectangle>(levels);
                int    offset  = 0;

                for (int i = 0; i < levels; i++)
                {
                    int mw = Math.Max(1, cwTex.Width  >> i);
                    int mh = Math.Max(1, cwTex.Height >> i);
                    int rowPitch, slicePitch;

                    if (isBC)
                    {
                        int blockSize = (fmt == Format.BC1_UNorm || fmt == Format.BC4_UNorm) ? 8 : 16;
                        rowPitch   = Math.Max(1, (mw + 3) / 4) * blockSize;
                        slicePitch = rowPitch * Math.Max(1, (mh + 3) / 4);
                    }
                    else
                    {
                        rowPitch   = mw * 4;
                        slicePitch = rowPitch * mh;
                    }

                    if (offset + slicePitch > bytes.Length) break;
                    rects.Add(new DataRectangle(basePtr + offset, rowPitch));
                    offset += slicePitch;
                }

                if (rects.Count == 0) return;

                var texDesc = new Texture2DDescription
                {
                    Width             = cwTex.Width,
                    Height            = cwTex.Height,
                    MipLevels         = rects.Count,
                    ArraySize         = 1,
                    Format            = fmt,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage             = ResourceUsage.Immutable,
                    BindFlags         = BindFlags.ShaderResource
                };

                // D3D11 reads from pinned memory synchronously here
                using var tex2d = new Texture2D(_device, texDesc, rects.ToArray());
                var srv = new ShaderResourceView(_device, tex2d);

                if (_textures.TryGetValue(cwTex.Name, out var old)) old.Dispose();
                _textures[cwTex.Name] = srv;
            }
            catch { /* skip unreadable mip data */ }
            finally
            {
                pinned.Free(); // safe — Texture2D already constructed above
            }
        }

        // -------------------------------------------------------------------------
        // Render loop
        // -------------------------------------------------------------------------
        public void Render()
        {
            if (_rtv == null || _dsv == null) return;

            _context.ClearRenderTargetView(_rtv, new Color4(0.08f, 0.09f, 0.11f, 1f));
            _context.ClearDepthStencilView(_dsv,
                DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1f, 0);

            if (_geoms.Count == 0 || _rtTex == null) goto Flush;

            // Camera — orbit + pan
            float aspect = (float)_rtTex.Description.Width / _rtTex.Description.Height;
            float cy = MathF.Cos(CameraYaw),   sy = MathF.Sin(CameraYaw);
            float cp = MathF.Cos(CameraPitch),  sp = MathF.Sin(CameraPitch);

            var forward = new Vector3(sy * cp, sp, cy * cp);
            var right   = Vector3.Normalize(Vector3.Cross(Vector3.Up, forward));
            var up      = Vector3.Normalize(Vector3.Cross(forward, right));

            var target = _modelCenter + right * PanX + up * PanY;
            var camPos = target + forward * CameraDistance;

            var view = Matrix.LookAtLH(camPos, target, Vector3.Up);
            var proj = Matrix.PerspectiveFovLH(MathF.PI / 4f, aspect, 0.01f, _modelRadius * 500f);

            var wvp = view * proj;
            Matrix.Transpose(ref wvp, out wvp);

            // Set constant buffer — HasTexture is set per-draw-call below
            var sc = new SceneConstants
            {
                WorldViewProj = wvp,
                LightDir      = Vector3.Normalize(new Vector3(-0.6f, -1f, 0.5f)),
                HasTexture    = 0f,
                Ambient       = new Vector4(0.55f, 0.55f, 0.58f, 1f)
            };
            _context.UpdateSubresource(ref sc, _cb);

            _context.VertexShader.Set(_vs);
            _context.VertexShader.SetConstantBuffer(0, _cb);
            _context.PixelShader.Set(_ps);
            _context.PixelShader.SetConstantBuffer(0, _cb);
            _context.PixelShader.SetSampler(0, _sampler);
            _context.Rasterizer.State = _rs;
            _context.Rasterizer.SetViewport(new Viewport(0, 0,
                _rtTex.Description.Width, _rtTex.Description.Height));
            _context.OutputMerger.SetTargets(_dsv, _rtv);
            _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

            foreach (var g in _geoms)
            {
                if (g.Layout == null || g.VertexBuffer == null || g.IndexBuffer == null) continue;

                // Per-geometry texture: look up by name, fall back to null
                ShaderResourceView? srv = null;
                if (g.TextureName != null)
                    _textures.TryGetValue(g.TextureName, out srv);

                // Update HasTexture flag and bind SRV
                sc.HasTexture = srv != null ? 1f : 0f;
                _context.UpdateSubresource(ref sc, _cb);
                _context.PixelShader.SetShaderResource(0, srv);

                _context.InputAssembler.InputLayout = g.Layout;
                _context.InputAssembler.SetVertexBuffers(0,
                    new VertexBufferBinding(g.VertexBuffer, g.VertexStride, 0));
                _context.InputAssembler.SetIndexBuffer(g.IndexBuffer, Format.R16_UInt, 0);
                _context.DrawIndexed(g.IndexCount, 0, 0);
            }

            Flush:
            _context.Flush();
            _imageSource.Invalidate();
        }

        private void ClearGeometries()
        {
            foreach (var g in _geoms) g.Dispose();
            _geoms.Clear();
        }

        public void Dispose()
        {
            ClearGeometries();
            foreach (var srv in _textures.Values) srv.Dispose();
            _textures.Clear();
            _sampler.Dispose();
            _rs.Dispose();
            _cb.Dispose();
            _vs?.Dispose();
            _ps?.Dispose();
            _rtv?.Dispose();
            _rtTex?.Dispose();
            _dsv?.Dispose();
            _depthTex?.Dispose();
            _imageSource.Dispose();
            _context.Dispose();
            _device.Dispose();
        }
    }
}
