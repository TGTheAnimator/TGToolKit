using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using CodeWalker.GameFiles;
using NAudio.Wave;

namespace ToolKitV.Views
{
    public partial class AudioViewer : UserControl, IDisposable
    {
        private IWavePlayer? _waveOut;
        private WaveFileReader? _waveReader;
        private DispatcherTimer _timer;
        private bool _isSliderDragging = false;

        public AudioViewer()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += Timer_Tick;
            
            LoadAwcBtn.Click += LoadAwcBtn_Click;
            StreamList.SelectionChanged += StreamList_SelectionChanged;
            PlayPauseBtn.Click += PlayPauseBtn_Click;
            PlaybackSlider.PreviewMouseDown += (s, e) => _isSliderDragging = true;
            PlaybackSlider.PreviewMouseUp += (s, e) => { _isSliderDragging = false; Seek(); };
        }

        private void LoadAwcBtn_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "GTA V Audio Container (*.awc)|*.awc|All files (*.*)|*.*",
                Title = "Select an AWC File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    byte[] data = File.ReadAllBytes(openFileDialog.FileName);
                    var entry = new RpfBinaryFileEntry();
                    entry.Name = Path.GetFileName(openFileDialog.FileName);
                    var awc = new AwcFile();
                    awc.Load(data, entry);

                    if (awc.Streams != null)
                    {
                        StreamList.ItemsSource = awc.Streams.ToList();
                        CurrentlyPlayingText.Text = $"Loaded {awc.Streams.Length} streams from {Path.GetFileName(openFileDialog.FileName)}";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load AWC: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void StreamList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StreamList.SelectedItem is AwcStream stream)
            {
                PlayStream(stream);
            }
        }

        private void PlayStream(AwcStream stream)
        {
            StopPlayback();

            try
            {
                byte[] wavData = stream.GetWavFile();
                if (wavData == null || wavData.Length == 0) return;

                var ms = new MemoryStream(wavData);
                _waveReader = new WaveFileReader(ms);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_waveReader);
                _waveOut.Play();
                
                _timer.Start();
                PlayPauseBtn.Content = "⏸";
                CurrentlyPlayingText.Text = $"Playing: {stream.Name}";
                
                PlaybackSlider.Maximum = _waveReader.TotalTime.TotalSeconds;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to play stream: {ex.Message}", "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_waveOut == null) return;

            if (_waveOut.PlaybackState == PlaybackState.Playing)
            {
                _waveOut.Pause();
                PlayPauseBtn.Content = "▶";
            }
            else
            {
                _waveOut.Play();
                PlayPauseBtn.Content = "⏸";
            }
        }

        private void Seek()
        {
            if (_waveReader != null)
            {
                _waveReader.CurrentTime = TimeSpan.FromSeconds(PlaybackSlider.Value);
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_waveReader != null && !_isSliderDragging)
            {
                PlaybackSlider.Value = _waveReader.CurrentTime.TotalSeconds;
                TimeText.Text = $"{_waveReader.CurrentTime:mm\\:ss} / {_waveReader.TotalTime:mm\\:ss}";
                
                if (_waveReader.CurrentTime >= _waveReader.TotalTime)
                {
                    StopPlayback();
                }
            }
        }

        private void StopPlayback()
        {
            _timer.Stop();
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveReader?.Dispose();
            _waveOut = null;
            _waveReader = null;
            PlayPauseBtn.Content = "▶";
            PlaybackSlider.Value = 0;
        }

        public void Dispose()
        {
            StopPlayback();
        }
    }
}
