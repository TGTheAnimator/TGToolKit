using System.Windows;
using System.Windows.Input;
using ToolKitV.Views;
using ToolKitV.Models.Providers;

namespace ToolKitV
{
    public partial class MainWindow : Window
    {
        // Pre-instantiate views so state is preserved when switching tabs
        private readonly TextureOptimization _textureView      = new();
        private readonly VehicleTools        _vehicleView      = new();
        private readonly AssetAnalyzer       _assetView        = new();

        private readonly ClothingTools       _clothingView     = new();
        private readonly AudioViewer         _audioView        = new();
        private readonly YtdSplitter         _ytdSplitView     = new();
        private readonly ServerLinter        _serverLinterView = new();
        private readonly SirenBuilder        _sirenBuilderView = new();
        private readonly RecipeStudio        _recipeStudioView = new();
        private readonly ItemImporter        _itemImporterView = new();
        private readonly SqlMatrix           _sqlMatrixView    = new();
        private readonly EconomyDashboard    _economyView      = new();
        private readonly GlobalTranspilerView _globalTranspilerView;


        public MainWindow()
        {
            InitializeComponent();
            _globalTranspilerView = new GlobalTranspilerView(_serverLinterView);

            // Start on Texture Optimizer
            MainContent.Content = _textureView;

            // Wire up menu navigation
            SideMenu.NavigateTo += OnNavigateTo;
            
            // Inject Linter reference so ItemImporter can grab SFTP credentials and paths
            _itemImporterView.RegisterLinterReference(_serverLinterView);
        }

        public string GetSftpPassword() => _serverLinterView.GetSftpPassword();

        private void OnNavigateTo(string view)
        {
            switch (view)
            {
                case "TextureOptimizer":
                    MainContent.Content  = _textureView;
                    AppSubtitle.Text     = "  FiveM Texture Optimizer";
                    break;

                case "VehicleTools":
                    MainContent.Content  = _vehicleView;
                    AppSubtitle.Text     = "  Vehicle Meta Consolidation";
                    break;

                case "AssetAnalyzer":
                    MainContent.Content  = _assetView;
                    AppSubtitle.Text     = "  Resource Budget Analyzer";
                    break;


                case "ClothingTools":
                    MainContent.Content  = _clothingView;
                    AppSubtitle.Text     = "  Add-on Clothing Generator";
                    break;

                case "AudioViewer":
                    MainContent.Content  = _audioView;
                    AppSubtitle.Text     = "  AWC Audio Previewer";
                    break;

                case "YtdSplitter":
                    MainContent.Content  = _ytdSplitView;
                    AppSubtitle.Text     = "  YTD Texture Splitter";
                    break;

                case "ServerLinter":
                    MainContent.Content  = _serverLinterView;
                    AppSubtitle.Text     = "  Server Linter";
                    break;

                case "SirenBuilder":
                    MainContent.Content  = _sirenBuilderView;
                    AppSubtitle.Text     = "  Visual Siren Builder";
                    break;

                case "RecipeStudio":
                    MainContent.Content  = _recipeStudioView;
                    AppSubtitle.Text     = "  Visual Recipe Studio";
                    break;

                case "ItemImporter":
                    MainContent.Content  = _itemImporterView;
                    AppSubtitle.Text     = "  Asset & Item Importer";
                    break;

                case "SqlMatrix":
                    MainContent.Content  = _sqlMatrixView;
                    AppSubtitle.Text     = "  SQL Migration Matrix";
                    _sqlMatrixView.UpdateSftpInfo();
                    break;
                    
                case "GlobalTranspiler":
                    MainContent.Content  = _globalTranspilerView;
                    AppSubtitle.Text     = "  Global Transpiler Engine";
                    break;


                case "Economy":
                    MainContent.Content  = _economyView;
                    AppSubtitle.Text     = "  Economy & Loot Balancer";
                    
                    IFileSystemProvider fsProvider;
                    string rootPath;
                    
                    if (_serverLinterView.IsSftpMode())
                    {
                        var cfg = ToolKitV.Models.LinterConfig.Load();
                        fsProvider = new SftpFileSystemProvider(cfg.Host, cfg.Port, cfg.Username, _serverLinterView.GetSftpPassword());
                        rootPath = cfg.RootPath;
                    }
                    else
                    {
                        fsProvider = new LocalFileSystemProvider();
                        rootPath = _serverLinterView.GetLocalFolder();
                    }
                    
                    _economyView.InitializeDashboard(fsProvider, rootPath);
                    break;
            }
        }

        private void StackPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}
