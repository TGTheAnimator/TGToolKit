using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToolKitV.Rendering;
using ToolKitV.Models.Rendering;
using SharpDX;
using Point = System.Windows.Point;

namespace ToolKitV.Views
{
    public partial class ViewportControl : UserControl, IDisposable
    {
        private Renderer? _renderer;
        private bool _isActive = false;

        private Point _lastMousePos;
        private bool _isDragging = false;
        private bool _isPanning  = false;

        // FPS calculation
        private Stopwatch _fpsStopwatch = new Stopwatch();
        private int _frameCount = 0;
        private double _lastFpsUpdate = 0;

        public ViewportControl()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
                return;

            try
            {
                _renderer = new Renderer();
                ViewportImage.Source = _renderer.ImageSource.ImageSource;
                
                // Hook into WPF rendering loop for 60fps
                CompositionTarget.Rendering += CompositionTarget_Rendering;
                
                _isActive = true;
                _fpsStopwatch.Start();
                UpdateSize();
                UpdateLuaCode();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize 3D Viewport: {ex.Message}", "DirectX Error", MessageBoxButton.OK, MessageBoxImage.Error);
                OverlayBorder.Visibility = Visibility.Visible;
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSize();
        }

        private void UpdateSize()
        {
            if (_isActive && _renderer != null && ActualWidth > 0 && ActualHeight > 0)
            {
                // Multi-DPI Support: Calculate physical pixels
                var presentationSource = PresentationSource.FromVisual(this);
                double dpiX = 1.0;
                double dpiY = 1.0;

                if (presentationSource?.CompositionTarget != null)
                {
                    dpiX = presentationSource.CompositionTarget.TransformToDevice.M11;
                    dpiY = presentationSource.CompositionTarget.TransformToDevice.M22;
                }

                int pixelWidth = (int)(ActualWidth * dpiX);
                int pixelHeight = (int)(ActualHeight * dpiY);

                _renderer.Resize(pixelWidth, pixelHeight);
            }
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_isActive && _renderer != null)
            {
                // 15% interpolation per frame gives a sharp, modern weight to the camera
                _renderer.LerpCamera(0.15f);
                _renderer.Render();
                UpdateFps();
            }
        }

        private void UpdateFps()
        {
            _frameCount++;
            double elapsed = _fpsStopwatch.Elapsed.TotalSeconds;
            if (elapsed - _lastFpsUpdate >= 1.0)
            {
                double fps = _frameCount / (elapsed - _lastFpsUpdate);
                FpsText.Text = $"{fps:F0} FPS";
                _frameCount = 0;
                _lastFpsUpdate = elapsed;
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_renderer == null) return;

            // Grab focus so that keyboard events (like Ctrl+Z and snap keys) route correctly
            Focus();

            // Place a point on Shift + Left Click
            if (Keyboard.IsKeyDown(Key.LeftShift))
            {
                Point p = e.GetPosition(this);
                // Multi-DPI Support: Calculate physical pixels
                var presentationSource = PresentationSource.FromVisual(this);
                double dpiX = 1.0;
                double dpiY = 1.0;

                if (presentationSource?.CompositionTarget != null)
                {
                    dpiX = presentationSource.CompositionTarget.TransformToDevice.M11;
                    dpiY = presentationSource.CompositionTarget.TransformToDevice.M22;
                }

                int pixelX = (int)(p.X * dpiX);
                int pixelY = (int)(p.Y * dpiY);
                int screenW = (int)(ActualWidth * dpiX);
                int screenH = (int)(ActualHeight * dpiY);

                Vector3? hit = null;

                // God-Tier Vertex Snapping: Alt + Shift snaps directly to 3D geometry!
                if (Keyboard.IsKeyDown(Key.LeftAlt))
                {
                    hit = _renderer.GetNearestVertexSnap(pixelX, pixelY, screenW, screenH);
                }
                else
                {
                    hit = _renderer.GetMouseFloorIntersection(pixelX, pixelY, screenW, screenH);
                }

                if (hit.HasValue)
                {
                    if (_renderer.CurrentZoneType == Renderer.TargetZoneType.BoxZone)
                    {
                        _renderer.ZonePoints.Clear(); // BoxZone only allows 1 center point
                    }

                    _renderer.ZonePoints.Add(hit.Value);
                    UpdateLuaCode();
                }
                return; // Skip camera orbit tracking
            }

            _renderer.TargetCameraYaw   = _renderer.CameraYaw;
            _renderer.TargetCameraPitch = _renderer.CameraPitch;
            _isDragging = true;
            _lastMousePos = e.GetPosition(this);
            CaptureMouse();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _isDragging = false;
            if (!_isPanning) ReleaseMouseCapture();
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (_renderer == null) return;

            // Clear the zone on Shift + Right Click
            if (Keyboard.IsKeyDown(Key.LeftShift))
            {
                _renderer.ZonePoints.Clear();
                UpdateLuaCode();
                return; // Skip panning tracking
            }

            _renderer.TargetPanX = _renderer.PanX;
            _renderer.TargetPanY = _renderer.PanY;
            _isPanning = true;
            _lastMousePos = e.GetPosition(this);
            CaptureMouse();
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            _isPanning = false;
            if (!_isDragging) ReleaseMouseCapture();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_renderer == null) return;

            var pos = e.GetPosition(this);
            float dx = (float)(pos.X - _lastMousePos.X);
            float dy = (float)(pos.Y - _lastMousePos.Y);

            if (_isDragging)
            {
                // Orbit — update target variables, NOT the actual camera directly
                float sens = 0.005f;
                _renderer.TargetCameraYaw   -= dx * sens;
                _renderer.TargetCameraPitch -= dy * sens;
                // No Pitch clamps! Allow free 360-degree matrix-driven free-look rotation!
            }

            if (_isPanning)
            {
                // Pan — scale by target distance so panning feels consistent across zoom levels
                float panScale = _renderer.TargetCameraDistance * 0.001f;
                _renderer.TargetPanX -= dx * panScale;
                _renderer.TargetPanY += dy * panScale;
            }

            _lastMousePos = pos;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_renderer != null)
            {
                // Zoom target camera distance proportionally
                float zoomFactor = 1f - (e.Delta / 1200f);
                _renderer.TargetCameraDistance = Math.Max(0.1f, _renderer.TargetCameraDistance * zoomFactor);
            }
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            OverlayBorder.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            if (active) _fpsStopwatch.Restart();
            else _fpsStopwatch.Stop();
        }

        public void LoadDrawable(CodeWalker.GameFiles.DrawableBase drawable)
        {
            _renderer?.LoadDrawable(drawable);
        }

        public void LoadTexture(CodeWalker.GameFiles.Texture texture)
        {
            _renderer?.LoadTexture(texture);
        }

        public void Dispose()
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _isActive = false;
            _fpsStopwatch.Stop();
            
            if (_renderer != null)
            {
                ViewportImage.Source = null;
                _renderer.Dispose();
                _renderer = null;
            }
        }

        private void ZoneMode_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_renderer == null || sldWidth == null) return;

            bool isBox = rbBoxZone.IsChecked == true;
            _renderer.CurrentZoneType = isBox ? Renderer.TargetZoneType.BoxZone : Renderer.TargetZoneType.PolyZone;
            _renderer.ZonePoints.Clear(); // Reset points when switching modes

            // Toggle Box UI Elements
            var vis = isBox ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            lblWidth.Visibility = sldWidth.Visibility = valWidth.Visibility = vis;
            lblLength.Visibility = sldLength.Visibility = valLength.Visibility = vis;
            lblHeading.Visibility = sldHeading.Visibility = valHeading.Visibility = vis;

            UpdateLuaCode();
        }

        private void UI_ZoneParamChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (_renderer == null) return;

            // Push UI values to the DirectX renderer
            _renderer.ZoneHeight = (float)sldHeight.Value;
            _renderer.BoxWidth = (float)sldWidth.Value;
            _renderer.BoxLength = (float)sldLength.Value;
            _renderer.BoxHeading = (float)sldHeading.Value;

            UpdateLuaCode();
        }

        private void UpdateLuaCode()
        {
            if (_renderer == null) return;

            if (_renderer.ZonePoints.Count == 0)
            {
                txtLuaOutput.Text = "-- Shift+Click to place a zone.";
                return;
            }

            var sb = new System.Text.StringBuilder();

            if (_renderer.CurrentZoneType == Renderer.TargetZoneType.BoxZone)
            {
                var c = _renderer.ZonePoints[0];
                // In DirectX: Y is vertical UP axis, X and Z are horizontal
                // ox_target BoxZone coordinates in GTA V space (which is Z-Up):
                //   GTA V X = DirectX X
                //   GTA V Y = DirectX Z
                //   GTA V Z = DirectX Y (adjusted by height/2 so it sits in the middle of the box)
                float centerZ = c.Y + (_renderer.ZoneHeight / 2f);

                sb.AppendLine("exports.ox_target:addBoxZone({");
                sb.AppendLine($"    coords = vec3({c.X:F2}, {c.Z:F2}, {centerZ:F2}),");
                sb.AppendLine($"    size = vec3({_renderer.BoxWidth:F2}, {_renderer.BoxLength:F2}, {_renderer.ZoneHeight:F2}),");
                sb.AppendLine($"    rotation = {_renderer.BoxHeading:F0},");
                sb.AppendLine("    debug = true,");
                sb.AppendLine("    options = {");
                sb.AppendLine("        {");
                sb.AppendLine("            name = 'box_zone',");
                sb.AppendLine("            event = 'my:event',");
                sb.AppendLine("            icon = 'fa-solid fa-box',");
                sb.AppendLine("            label = 'Interact',");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
                sb.AppendLine("})");
            }
            else
            {
                sb.AppendLine("exports.ox_target:addPolyZone({");
                sb.AppendLine("    points = {");
                foreach (var pt in _renderer.ZonePoints)
                {
                    // DirectX -> GTA V/FiveM coordinates conversion:
                    //   GTA V X = DirectX X
                    //   GTA V Y = DirectX Z
                    //   GTA V Z = DirectX Y
                    sb.AppendLine($"        vec3({pt.X:F2}, {pt.Z:F2}, {pt.Y:F2}),");
                }
                sb.AppendLine("    },");
                sb.AppendLine($"    thickness = {_renderer.ZoneHeight:F2},");
                sb.AppendLine("    debug = true,");
                sb.AppendLine("    options = {");
                sb.AppendLine("        {");
                sb.AppendLine("            name = 'poly_zone',");
                sb.AppendLine("            event = 'my:event',");
                sb.AppendLine("            icon = 'fa-solid fa-draw-polygon',");
                sb.AppendLine("            label = 'Interact',");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
                sb.AppendLine("})");
            }

            txtLuaOutput.Text = sb.ToString();
        }

        private void btnCopyCode_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtLuaOutput.Text);
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Catch Ctrl + Z to undo the last clicked point
            if (e.Key == System.Windows.Input.Key.Z && 
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (_renderer != null && _renderer.ZonePoints.Count > 0)
                {
                    _renderer.ZonePoints.RemoveAt(_renderer.ZonePoints.Count - 1);
                    UpdateLuaCode(); 
                    e.Handled = true;
                }
            }
        }
    }
}
