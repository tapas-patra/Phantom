using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using SecureOverlay.Platform.Windows;

namespace SecureOverlay
{
    public partial class MicrophonePermissionWindow : Window
    {
        public bool PermissionGranted { get; private set; } = false;
        private TaskCompletionSource<bool> _permissionTask;

        public MicrophonePermissionWindow()
        {
            InitializeComponent();
            WindowProtection.MakeInvisibleToScreenCapture(this);
            _permissionTask = new TaskCompletionSource<bool>();
            
            Loaded += MicrophonePermissionWindow_Loaded;
        }

        private async void MicrophonePermissionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("MICROPHONE PERMISSION WINDOW OPENED");
            Log.WriteLine("═══════════════════════════════════════════════════════");

            try
            {
                var userDataFolder = WindowsAppPaths.WebView2CachePath;

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await PermissionWebView.EnsureCoreWebView2Async(env);

                Log.WriteLine("✓ Permission browser initialized");

                // Handle permission requests - DON'T auto-grant, let the UI show
                PermissionWebView.CoreWebView2.PermissionRequested += (s, args) =>
                {
                    Log.WriteLine($"🔐 Permission requested: {args.PermissionKind}");
                    
                    if (args.PermissionKind == CoreWebView2PermissionKind.Microphone)
                    {
                        Log.WriteLine("  → Showing native browser permission dialog");
                        // DON'T set args.State - this lets the browser show its UI
                    }
                };

                // Listen for messages from the page
                PermissionWebView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    var message = args.TryGetWebMessageAsString();
                    Log.WriteLine($"Permission browser → C#: {message}");

                    if (message == "PERMISSION:granted")
                    {
                        Log.WriteLine("✅ PERMISSION GRANTED!");
                        PermissionGranted = true;
                        StatusText.Text = "✅ Permission granted!";
                        
                        Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            await Task.Delay(1000);
                            _permissionTask.TrySetResult(true);
                            DialogResult = true;
                            Close();
                        }));
                    }
                    else if (message == "PERMISSION:denied")
                    {
                        Log.WriteLine("❌ Permission denied by user");
                        StatusText.Text = "❌ Permission denied - please click Allow";
                        PermissionGranted = false;
                    }
                };

                Log.WriteLine("Creating temporary HTML file...");
                StatusText.Text = "Loading...";
                
                // Save HTML to a temporary file (file:// is a secure context)
                var tempFolder = WindowsAppPaths.TempRoot;
                
                if (!System.IO.Directory.Exists(tempFolder))
                {
                    System.IO.Directory.CreateDirectory(tempFolder);
                }

                var htmlFilePath = WindowsAppPaths.MicrophonePermissionHtmlPath;
                System.IO.File.WriteAllText(htmlFilePath, GetPermissionHTML());
                
                Log.WriteLine($"  HTML saved to: {htmlFilePath}");
                Log.WriteLine("Navigating to file...");
                
                // Navigate to the file using file:// protocol (secure context)
                PermissionWebView.CoreWebView2.Navigate($"file:///{htmlFilePath.Replace("\\", "/")}");
                
                StatusText.Text = "Click the button to grant permission";
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Permission window error: {ex.Message}");
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private string GetPermissionHTML()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>Microphone Permission</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 30px;
            height: 100vh;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
        }
        .container {
            text-align: center;
            max-width: 500px;
        }
        h1 {
            font-size: 28px;
            margin-bottom: 15px;
        }
        .icon {
            font-size: 64px;
            margin-bottom: 20px;
            animation: pulse 2s infinite;
        }
        @keyframes pulse {
            0%, 100% { transform: scale(1); }
            50% { transform: scale(1.1); }
        }
        p {
            font-size: 15px;
            line-height: 1.6;
            margin-bottom: 25px;
            opacity: 0.9;
        }
        .btn {
            background: white;
            color: #667eea;
            border: none;
            padding: 15px 40px;
            font-size: 18px;
            font-weight: bold;
            border-radius: 30px;
            cursor: pointer;
            box-shadow: 0 4px 15px rgba(0,0,0,0.2);
            transition: all 0.3s;
        }
        .btn:hover {
            transform: translateY(-2px);
            box-shadow: 0 6px 20px rgba(0,0,0,0.3);
        }
        .btn:active {
            transform: translateY(0);
        }
        .btn:disabled {
            opacity: 0.5;
            cursor: not-allowed;
        }
        #status {
            margin-top: 20px;
            padding: 15px;
            background: rgba(255,255,255,0.2);
            border-radius: 10px;
            font-size: 14px;
            min-height: 50px;
        }
        .success { color: #4ade80; font-weight: bold; }
        .error { color: #f87171; font-weight: bold; }
        .note {
            font-size: 13px;
            margin-top: 15px;
            opacity: 0.8;
            font-style: italic;
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='icon'>🎤</div>
        <h1>Microphone Access Needed</h1>
        <p>
            This app needs access to your microphone for voice input.<br>
            <br>
            Click the button below, then click <strong>""Allow""</strong> when prompted.
        </p>
        <button class='btn' onclick='requestPermission()' id='requestBtn'>
            Grant Microphone Access
        </button>
        <div id='status'></div>
        <p class='note'>✅ You only need to do this once - permission is saved permanently</p>
    </div>

    <script>
        console.log('Permission page loaded');
        console.log('Location:', window.location.href);
        console.log('isSecureContext:', window.isSecureContext);
        
        const statusEl = document.getElementById('status');
        const btnEl = document.getElementById('requestBtn');

        // Check if mediaDevices is available
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            statusEl.textContent = '❌ MediaDevices API not available';
            statusEl.className = 'error';
            btnEl.disabled = true;
            console.error('MediaDevices not available!');
        }

        async function requestPermission() {
            try {
                statusEl.textContent = '🔄 Requesting permission...';
                statusEl.className = '';
                btnEl.disabled = true;

                console.log('Requesting microphone access...');
                
                if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                    throw new Error('getUserMedia not supported');
                }
                
                // This will trigger the browser's permission prompt
                const stream = await navigator.mediaDevices.getUserMedia({ 
                    audio: true 
                });

                console.log('✅ Permission granted!', stream);
                statusEl.textContent = '✅ Permission granted! Voice input is ready.';
                statusEl.className = 'success';

                // Stop the stream (we just needed permission)
                stream.getTracks().forEach(track => {
                    console.log('Stopping track:', track);
                    track.stop();
                });
                console.log('Stream stopped');

                // Notify the C# app
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage('PERMISSION:granted');
                } else {
                    console.error('WebView2 API not available');
                }

            } catch (err) {
                console.error('❌ Permission error:', err);
                
                if (err.name === 'NotAllowedError') {
                    statusEl.textContent = '❌ Permission denied. Please click ""Allow"" when asked.';
                } else if (err.name === 'NotFoundError') {
                    statusEl.textContent = '❌ No microphone found. Please connect a microphone.';
                } else if (err.name === 'NotSupportedError') {
                    statusEl.textContent = '❌ Not supported in this context.';
                } else {
                    statusEl.textContent = '❌ Error: ' + err.message;
                }
                
                statusEl.className = 'error';
                btnEl.disabled = false;

                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage('PERMISSION:denied');
                }
            }
        }

        window.addEventListener('load', () => {
            console.log('Page fully loaded');
            if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
                statusEl.textContent = 'Ready! Click the button above.';
            }
        });
    </script>
</body>
</html>";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Permission request cancelled by user");
            PermissionGranted = false;
            _permissionTask.TrySetResult(false);
            DialogResult = false;
            Close();
        }

        public Task<bool> WaitForPermissionAsync()
        {
            return _permissionTask.Task;
        }
    }
}
