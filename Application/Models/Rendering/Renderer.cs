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
    [StructLayout(LayoutKind.Explicit, Size = 112)] // Increased size to 112 bytes
    struct SceneConstants
    {
        [FieldOffset(0)]   public Matrix  WorldViewProj;
        [FieldOffset(64)]  public Vector3 LightDir;
        [FieldOffset(76)]  public float   HasTexture;
        [FieldOffset(80)]  public Vector4 Ambient;
        [FieldOffset(96)]  public float   IsGrid;       // <--- NEW
        [FieldOffset(100)] public Vector3 Padding;
    }

    // Struct for our Grid vertices so they match the HLSL VS_IN perfectly
    [StructLayout(LayoutKind.Sequential)]
    struct GridVertex
    {
        public Vector3 Position;
        public Vector4 Normal;
        public Vector2 TexCoord;
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
        public List<Vector3> CachedPositions { get; } = new List<Vector3>();

        public void Dispose()
        {
            Layout?.Dispose();
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
            CachedPositions.Clear();
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
        private Buffer?          _cb;
        private SamplerState?    _sampler;
        private RasterizerState? _rsSolid;      // Upgraded
        private RasterizerState? _rsWireframe;  // Upgraded
        private byte[]?         _vsBlob;

        // CAD Grid resources
        private Buffer?      _gridVb;
        private int          _gridVertexCount;
        private InputLayout? _gridLayout;

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

        // Target Camera properties (interpolated via LerpCamera)
        public float TargetCameraYaw      { get; set; } = MathF.PI * 0.25f;
        public float TargetCameraPitch    { get; set; } = 0.35f;
        public float TargetCameraDistance { get; set; } = 5.0f;
        public float TargetPanX           { get; set; } = 0f;
        public float TargetPanY           { get; set; } = 0f;

        public bool IsWireframeMode { get; set; } = false;

        // A list to hold the points the user clicks for PolyZone building
        public List<Vector3> ZonePoints { get; } = new List<Vector3>();

        public enum TargetZoneType { PolyZone, BoxZone }
        public TargetZoneType CurrentZoneType { get; set; } = TargetZoneType.PolyZone;
        public float ZoneHeight { get; set; } = 2.0f;
        public float BoxWidth { get; set; } = 1.0f;
        public float BoxLength { get; set; } = 1.0f;
        public float BoxHeading { get; set; } = 0.0f; // In degrees

        private Vector3 _modelCenter = Vector3.Zero;
        private float   _modelRadius = 1.0f;

        public void LerpCamera(float amount = 0.15f)
        {
            CameraYaw      += (TargetCameraYaw      - CameraYaw)      * amount;
            CameraPitch    += (TargetCameraPitch    - CameraPitch)    * amount;
            CameraDistance += (TargetCameraDistance - CameraDistance) * amount;
            PanX           += (TargetPanX           - PanX)           * amount;
            PanY           += (TargetPanY           - PanY)           * amount;
        }

        public Vector3? GetMouseFloorIntersection(int mouseX, int mouseY, int screenWidth, int screenHeight)
        {
            if (_rtTex == null || screenWidth <= 0 || screenHeight <= 0) return null;

            float aspect = (float)screenWidth / screenHeight;
            Matrix camRotation = Matrix.RotationYawPitchRoll(CameraYaw, CameraPitch, 0);
            Vector3 forward = Vector3.TransformNormal(Vector3.ForwardLH, camRotation);
            Vector3 up      = Vector3.TransformNormal(Vector3.Up, camRotation);
            Vector3 right   = Vector3.TransformNormal(Vector3.Right, camRotation);

            var target = _modelCenter + right * PanX + up * PanY;
            var camPos = target - (forward * CameraDistance);
            
            var view = Matrix.LookAtLH(camPos, target, up);
            var proj = Matrix.PerspectiveFovLH(MathF.PI / 4f, aspect, 0.01f, _modelRadius * 500f);

            var ray = SharpDX.Ray.GetPickRay(
                mouseX, mouseY, 
                new SharpDX.ViewportF { X = 0, Y = 0, Width = screenWidth, Height = screenHeight }, 
                Matrix.Multiply(view, proj)
            );

            // In our DirectX coordinate system, the vertical axis is Y (UP)
            float floorY = _modelCenter.Y - _modelRadius;
            
            // Ray-to-Plane Intersection (Plane equation: Y = floorY)
            if (Math.Abs(ray.Direction.Y) < 0.0001f) return null; 

            float t = (floorY - ray.Position.Y) / ray.Direction.Y;
            
            // Only return if the intersection is in front of the camera
            if (t > 0)
            {
                return ray.Position + (ray.Direction * t);
            }

            return null;
        }

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

            _rsSolid = new RasterizerState(_device, new RasterizerStateDescription
            {
                FillMode             = FillMode.Solid,
                CullMode             = CullMode.None,
                IsDepthClipEnabled   = true,
                IsScissorEnabled     = false,
                IsMultisampleEnabled = true,
                IsAntialiasedLineEnabled = true
            });

            _rsWireframe = new RasterizerState(_device, new RasterizerStateDescription
            {
                FillMode             = FillMode.Wireframe,
                CullMode             = CullMode.None,
                IsDepthClipEnabled   = true,
                IsScissorEnabled     = false,
                IsMultisampleEnabled = true,
                IsAntialiasedLineEnabled = true
            });

            InitGridFloor();
        }

        private void InitGridFloor()
        {
            var vertices = new List<GridVertex>();
            int size = 50;        // How far the grid extends
            float step = 1.0f;    // 1 meter squares

            for (int i = -size; i <= size; i++)
            {
                // X-axis lines
                vertices.Add(new GridVertex { Position = new Vector3(i * step, 0, -size * step), Normal = Vector4.UnitY });
                vertices.Add(new GridVertex { Position = new Vector3(i * step, 0,  size * step), Normal = Vector4.UnitY });
                // Z-axis lines
                vertices.Add(new GridVertex { Position = new Vector3(-size * step, 0, i * step), Normal = Vector4.UnitY });
                vertices.Add(new GridVertex { Position = new Vector3( size * step, 0, i * step), Normal = Vector4.UnitY });
            }

            _gridVertexCount = vertices.Count;
            _gridVb = Buffer.Create(_device, BindFlags.VertexBuffer, vertices.ToArray());

            // Create a static layout for the grid
            var elements = new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("NORMAL", 0, Format.R32G32B32A32_Float, 12, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 28, 0) // Explicit TEXCOORD0 matching
            };
            _gridLayout = new InputLayout(_device, _vsBlob, elements);
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
                TargetCameraDistance = CameraDistance;
                PanX = 0f;
                PanY = 0f;
                TargetPanX = 0f;
                TargetPanY = 0f;
                CameraYaw = MathF.PI * 0.25f;
                CameraPitch = 0.35f;
                TargetCameraYaw = CameraYaw;
                TargetCameraPitch = CameraPitch;
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

                    var gpuData = new GeometryGpuData
                    {
                        VertexBuffer = vb,
                        IndexBuffer  = ib,
                        Layout       = layout,
                        VertexStride = vd.VertexStride,
                        IndexCount   = geom.IndexBuffer.Indices.Length,
                        TextureName  = textureName
                    };

                    // Extract positions for magnetic snapping (Assuming 32-bit Float3 at offset 0)
                    if (vd.VertexStride >= 12 && vd.VertexBytes != null && vd.VertexBytes.Length >= vd.VertexStride)
                    {
                        int numVertices = vd.VertexBytes.Length / vd.VertexStride;
                        for (int i = 0; i < numVertices; i++)
                        {
                            int startIdx = i * vd.VertexStride;
                            if (startIdx + 12 <= vd.VertexBytes.Length)
                            {
                                float vx = BitConverter.ToSingle(vd.VertexBytes, startIdx);
                                float vy = BitConverter.ToSingle(vd.VertexBytes, startIdx + 4);
                                float vz = BitConverter.ToSingle(vd.VertexBytes, startIdx + 8);
                                gpuData.CachedPositions.Add(new Vector3(vx, vy, vz));
                            }
                        }
                    }

                    _geoms.Add(gpuData);
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
            if (plist?.Parameters == null || plist.Hashes == null) return null;

            int count = Math.Min(plist.Parameters.Length, plist.Hashes.Length);

            // 1. Professional Tier: Search for parameters matching known diffuse sampler names
            for (int i = 0; i < count; i++)
            {
                var p = plist.Parameters[i];
                if (p.Data is Texture tex)
                {
                    string name = plist.Hashes[i].ToString();
                    if (name.Equals("DiffuseSampler", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Texture", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("diffusetexsampler", StringComparison.OrdinalIgnoreCase))
                    {
                        return tex.Name;
                    }
                }
            }

            // 2. Fallback: Grab the first parameter of DataType 0 that contains a Texture
            for (int i = 0; i < count; i++)
            {
                var p = plist.Parameters[i];
                if (p.DataType == 0 && p.Data is Texture tex)
                {
                    return tex.Name;
                }
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
            if (_rtv == null || _dsv == null || _rtTex == null) return;

            _context.ClearRenderTargetView(_rtv, new Color4(0.08f, 0.09f, 0.11f, 1f));
            _context.ClearDepthStencilView(_dsv,
                DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1f, 0);

            // --- UNRESTRICTED SPHERICAL CAMERA MATH ---
            float aspect = (float)_rtTex.Description.Width / _rtTex.Description.Height;

            // Calculate the exact rotation matrix from Yaw and Pitch
            Matrix camRotation = Matrix.RotationYawPitchRoll(CameraYaw, CameraPitch, 0);

            // Dynamically calculate our Up, Right, and Forward vectors based on rotation
            Vector3 forward = Vector3.TransformNormal(Vector3.ForwardLH, camRotation);
            Vector3 up      = Vector3.TransformNormal(Vector3.Up, camRotation);
            Vector3 right   = Vector3.TransformNormal(Vector3.Right, camRotation);

            var target = _modelCenter + right * PanX + up * PanY;
            var camPos = target - (forward * CameraDistance); // Pull back from target

            // Because 'up' is dynamic, this will NEVER Gimbal Lock!
            var view = Matrix.LookAtLH(camPos, target, up); 
            var proj = Matrix.PerspectiveFovLH(MathF.PI / 4f, aspect, 0.01f, _modelRadius * 500f);

            var wvp = view * proj;
            Matrix.Transpose(ref wvp, out wvp);

            // Set constant buffer — HasTexture and IsGrid are configured per-draw below
            var sc = new SceneConstants
            {
                WorldViewProj = wvp,
                LightDir      = Vector3.Normalize(new Vector3(-0.6f, -1f, 0.5f)),
                HasTexture    = 0f,
                IsGrid        = 0f,
                Ambient       = new Vector4(0.55f, 0.55f, 0.58f, 1f)
            };
            _context.UpdateSubresource(ref sc, _cb);

            _context.VertexShader.Set(_vs);
            _context.VertexShader.SetConstantBuffer(0, _cb);
            _context.PixelShader.Set(_ps);
            _context.PixelShader.SetConstantBuffer(0, _cb);
            _context.PixelShader.SetSampler(0, _sampler);

            // Apply Wireframe or Solid state
            _context.Rasterizer.State = IsWireframeMode ? _rsWireframe : _rsSolid;

            _context.Rasterizer.SetViewport(new Viewport(0, 0,
                _rtTex.Description.Width, _rtTex.Description.Height));
            _context.OutputMerger.SetTargets(_dsv, _rtv);

            // --- 1. DRAW THE GRID FIRST ---
            if (_gridVb != null && _gridLayout != null)
            {
                _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
                _context.InputAssembler.InputLayout = _gridLayout;
                _context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_gridVb, 36, 0));

                sc.IsGrid = 1f; // Tell HLSL to render grid lines
                _context.UpdateSubresource(ref sc, _cb);

                _context.Draw(_gridVertexCount, 0);
            }

            // --- 2. DRAW THE 3D MODELS ---
            if (_geoms.Count > 0)
            {
                _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                sc.IsGrid = 0f; // Turn off grid rendering for the models

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
            }

            // --- 3. DRAW THE TARGETING ZONE LINES ---
            DrawZoneLines(ref sc);

            _context.Flush();
            _imageSource.Invalidate();
        }

        private void DrawZoneLines(ref SceneConstants sc)
        {
            if (ZonePoints.Count == 0) return;

            var lines = new List<GridVertex>();

            // 1. Draw crosshairs for each point to give clear visual feedback
            float size = _modelRadius * 0.03f; // Scale crosshair size based on model bounds
            foreach (var pt in ZonePoints)
            {
                lines.Add(new GridVertex { Position = pt + new Vector3(-size, 0, 0), Normal = Vector4.UnitY });
                lines.Add(new GridVertex { Position = pt + new Vector3(size, 0, 0), Normal = Vector4.UnitY });
                lines.Add(new GridVertex { Position = pt + new Vector3(0, 0, -size), Normal = Vector4.UnitY });
                lines.Add(new GridVertex { Position = pt + new Vector3(0, 0, size), Normal = Vector4.UnitY });
            }

            if (CurrentZoneType == TargetZoneType.BoxZone && ZonePoints.Count > 0)
            {
                // ─── BOXZONE MATH: Calculate 8 corners of a rotated 3D Box ───
                Vector3 center = ZonePoints[0];
                
                // Convert heading to radians for math
                float rad = BoxHeading * (MathF.PI / 180f);
                float cos = MathF.Cos(rad);
                float sin = MathF.Sin(rad);

                // Half dimensions
                float hw = BoxWidth / 2f;
                float hl = BoxLength / 2f;

                // Unrotated relative corners (Bottom floor: Y = 0, XZ plane is horizontal)
                Vector3[] baseCorners = new Vector3[4]
                {
                    new Vector3(-hw, 0, -hl),
                    new Vector3(hw, 0, -hl),
                    new Vector3(hw, 0, hl),
                    new Vector3(-hw, 0, hl)
                };

                Vector3[] bottomCorners = new Vector3[4];
                Vector3[] topCorners = new Vector3[4];

                // Apply Yaw (Y-axis) rotation matrix and translation
                for (int i = 0; i < 4; i++)
                {
                    float rx = baseCorners[i].X * cos - baseCorners[i].Z * sin;
                    float rz = baseCorners[i].X * sin + baseCorners[i].Z * cos;
                    
                    bottomCorners[i] = new Vector3(center.X + rx, center.Y, center.Z + rz);
                    topCorners[i] = new Vector3(center.X + rx, center.Y + ZoneHeight, center.Z + rz);
                }

                // Build the Box Wireframe
                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    // Bottom square
                    lines.Add(new GridVertex { Position = bottomCorners[i], Normal = Vector4.UnitY });
                    lines.Add(new GridVertex { Position = bottomCorners[next], Normal = Vector4.UnitY });
                    // Top square
                    lines.Add(new GridVertex { Position = topCorners[i], Normal = Vector4.UnitY });
                    lines.Add(new GridVertex { Position = topCorners[next], Normal = Vector4.UnitY });
                    // Vertical pillars
                    lines.Add(new GridVertex { Position = bottomCorners[i], Normal = Vector4.UnitY });
                    lines.Add(new GridVertex { Position = topCorners[i], Normal = Vector4.UnitY });
                }
            }
            else if (CurrentZoneType == TargetZoneType.PolyZone && ZonePoints.Count >= 2)
            {
                // ─── POLYZONE MATH: 3D Extrusion of clicked points ───
                for (int i = 0; i < ZonePoints.Count; i++)
                {
                    Vector3 cb = ZonePoints[i];
                    Vector3 nb = ZonePoints[(i + 1) % ZonePoints.Count];
                    
                    Vector3 ct = new Vector3(cb.X, cb.Y + ZoneHeight, cb.Z);
                    Vector3 nt = new Vector3(nb.X, nb.Y + ZoneHeight, nb.Z);

                    // Bottom line, Top line, Vertical pillar
                    lines.Add(new GridVertex { Position = cb, Normal = Vector4.UnitY });
                    lines.Add(new GridVertex { Position = nb, Normal = Vector4.UnitY });
                    lines.Add(new GridVertex { Position = ct, Normal = Vector4.UnitY });
                    lines.Add(new GridVertex { Position = nt, Normal = Vector4.UnitY });
                    lines.Add(new GridVertex { Position = cb, Normal = Vector4.UnitY });
                    lines.Add(new GridVertex { Position = ct, Normal = Vector4.UnitY });
                }
            }

            if (lines.Count == 0) return;

            using var lineVb = Buffer.Create(_device, BindFlags.VertexBuffer, lines.ToArray());
            
            _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
            _context.InputAssembler.InputLayout = _gridLayout; // Reuse the grid layout
            _context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(lineVb, 36, 0));

            // Tell the shader to render this bright green instead of the grey models
            sc.IsGrid = 2f; // 2f represents fluorescent green in our HLSL
            _context.UpdateSubresource(ref sc, _cb);

            _context.Draw(lines.Count, 0);
        }

        public Vector3? GetNearestVertexSnap(int mouseX, int mouseY, int screenWidth, int screenHeight)
        {
            if (_rtTex == null || _geoms.Count == 0 || screenWidth <= 0 || screenHeight <= 0) return null;

            // 1. Build the Ray
            float aspect = (float)screenWidth / screenHeight;
            Matrix camRotation = Matrix.RotationYawPitchRoll(CameraYaw, CameraPitch, 0);
            Vector3 forward = Vector3.TransformNormal(Vector3.ForwardLH, camRotation);
            Vector3 up      = Vector3.TransformNormal(Vector3.Up, camRotation);
            Vector3 right   = Vector3.TransformNormal(Vector3.Right, camRotation);

            var target = _modelCenter + right * PanX + up * PanY;
            var camPos = target - (forward * CameraDistance);
            
            var view = Matrix.LookAtLH(camPos, target, up);
            var proj = Matrix.PerspectiveFovLH(MathF.PI / 4f, aspect, 0.01f, _modelRadius * 500f);

            var ray = SharpDX.Ray.GetPickRay(
                mouseX, mouseY, 
                new SharpDX.ViewportF { X = 0, Y = 0, Width = screenWidth, Height = screenHeight }, 
                Matrix.Multiply(view, proj)
            );

            Vector3? closestVertex = null;
            float minDistanceToRay = Math.Max(0.15f, _modelRadius * 0.08f); // Snap threshold
            float closestDepth = float.MaxValue;

            // 2. Scan every vertex in the loaded geometry
            foreach (var geom in _geoms)
            {
                foreach (Vector3 vertexPos in geom.CachedPositions) 
                {
                    // Check if vertex is in front of camera
                    Vector3 toVertex = vertexPos - camPos;
                    float depth = Vector3.Dot(toVertex, forward);
                    if (depth < 0) continue;

                    // Calculate perpendicular distance from the Ray line to the Vertex point
                    float distToRay = Vector3.Cross(ray.Direction, vertexPos - ray.Position).Length();

                    if (distToRay < minDistanceToRay && depth < closestDepth)
                    {
                        minDistanceToRay = distToRay;
                        closestDepth = depth;
                        closestVertex = vertexPos;
                    }
                }
            }

            return closestVertex;
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
            _sampler?.Dispose();
            _rsSolid?.Dispose();
            _rsWireframe?.Dispose();
            _gridVb?.Dispose();
            _gridLayout?.Dispose();
            _cb?.Dispose();
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
