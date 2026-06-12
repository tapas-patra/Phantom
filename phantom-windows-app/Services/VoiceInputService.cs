using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SecureOverlay.Platform.Windows;
using System.Windows;

namespace SecureOverlay.Services
{
    public class VoiceInputService : IDisposable
    {
        private WebView2? _webView;
        private System.Windows.Controls.Grid? _hostContainer;
        private bool _isListening = false;
        private bool _isInitialized = false;
        private bool _isInitializing = false;
        private bool _permissionGranted = false;
        private bool _isDisposed = false;
        private bool _browserProcessFailed = false;
        private EventHandler<CoreWebView2PermissionRequestedEventArgs>? _permissionRequestedHandler;
        private EventHandler<CoreWebView2ProcessFailedEventArgs>? _processFailedHandler;

        public event EventHandler<string>? SpeechRecognized;
        public event EventHandler<string>? StatusChanged;

        public VoiceInputService()
        {
            Log.WriteLine("VoiceInputService constructor");
        }

        public async Task<bool> InitializeAsync()
        {
            if (_isDisposed)
            {
                Log.WriteLine("Cannot initialize disposed voice service");
                return false;
            }

            if (_isInitialized)
            {
                Log.WriteLine("Already initialized");
                return true;
            }

            if (_isInitializing)
            {
                Log.WriteLine("Already initializing");
                return false;
            }

            _isInitializing = true;

            try
            {
                Log.WriteLine("════════════════════════════════════════════════");
                Log.WriteLine("Starting WebView2 initialization...");
                
                var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        Log.WriteLine("Step 1: Creating WebView2 instance...");
                        StatusChanged?.Invoke(this, "Creating browser...");
                        
                        _webView = new WebView2
                        {
                            Width = 1,
                            Height = 1,
                            Visibility = Visibility.Collapsed
                        };
                        
                        Log.WriteLine("Step 2: Adding WebView2 to visual tree...");
                        
                        var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                        if (mainWindow != null)
                        {
                            var container = mainWindow.FindName("WebView2Container") as System.Windows.Controls.Grid;
                            if (container != null)
                            {
                                _hostContainer = container;
                                container.Children.Add(_webView);
                                Log.WriteLine("  ✓ WebView2 added to MainWindow container");
                            }
                            else
                            {
                                throw new Exception("WebView2Container not found in MainWindow!");
                            }
                        }
                        else
                        {
                            throw new Exception("MainWindow not found!");
                        }
                        
                        Log.WriteLine("Step 3: Creating environment...");
                        StatusChanged?.Invoke(this, "Setting up environment...");
                        
                        var userDataFolder = WindowsAppPaths.WebView2CachePath;
                        
                        Log.WriteLine($"  User data folder: {userDataFolder}");
                        
                        var env = await CoreWebView2Environment.CreateAsync(
                            browserExecutableFolder: null,
                            userDataFolder: userDataFolder
                        );
                        
                        Log.WriteLine("  ✓ Environment created successfully");
                        
                        Log.WriteLine("Step 4: Initializing CoreWebView2...");
                        StatusChanged?.Invoke(this, "Initializing browser core...");
                        
                        await _webView.EnsureCoreWebView2Async(env);
                        
                        Log.WriteLine("  ✓✓✓ CoreWebView2 initialized successfully!");
                        
                        Log.WriteLine("Step 5: Configuring permissions...");

                        // Set up permission handler
                        _permissionRequestedHandler = OnPermissionRequested;
                        _webView.CoreWebView2.PermissionRequested += _permissionRequestedHandler;
                        _processFailedHandler = OnBrowserProcessFailed;
                        _webView.CoreWebView2.ProcessFailed += _processFailedHandler;

                        Log.WriteLine("  ✓ Permission handler configured");

                        Log.WriteLine("Step 6: Setting up message handler...");
                        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                        Log.WriteLine("Step 7: Loading speech recognition HTML...");
                        StatusChanged?.Invoke(this, "Loading speech engine...");

                        // Use file:// protocol (same as permission window for shared permissions)
                        var tempFolder = WindowsAppPaths.TempRoot;

                        if (!System.IO.Directory.Exists(tempFolder))
                        {
                            System.IO.Directory.CreateDirectory(tempFolder);
                        }

                        var htmlFilePath = WindowsAppPaths.SpeechRecognitionHtmlPath;
                        System.IO.File.WriteAllText(htmlFilePath, GetSpeechRecognitionHTML());

                        Log.WriteLine($"  HTML saved to: {htmlFilePath}");

                        // Navigate to file:// (secure context, shares permissions)
                        _webView.CoreWebView2.Navigate($"file:///{htmlFilePath.Replace("\\", "/")}");
                        
                        Log.WriteLine("Step 8: Waiting for page load...");
                        
                        var tcs = new TaskCompletionSource<bool>();
                        
                        void navigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
                        {
                            Log.WriteLine($"  Navigation completed. Success: {e.IsSuccess}");
                            if (e.IsSuccess)
                            {
                                tcs.TrySetResult(true);
                            }
                            else
                            {
                                Log.WriteLine($"  ✗ Navigation failed: {e.WebErrorStatus}");
                                tcs.TrySetResult(false);
                            }
                        }
                        
                        _webView.CoreWebView2.NavigationCompleted += navigationCompleted;
                        
                        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));
                        
                        _webView.CoreWebView2.NavigationCompleted -= navigationCompleted;
                        
                        if (completedTask == tcs.Task && await tcs.Task)
                        {
                            Log.WriteLine("  ✓ Page loaded successfully");
                            await Task.Delay(500);
                            
                            // Check if permission is already granted
                            try
                            {
                                var permResult = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                                    navigator.permissions.query({name:'microphone'}).then(r => r.state);
                                ");
                                Log.WriteLine($"  Microphone permission status: {permResult}");
                                _permissionGranted = permResult.Contains("granted");
                            }
                            catch
                            {
                                _permissionGranted = false;
                            }
                            
                            Log.WriteLine("════════════════════════════════════════════════");
                            Log.WriteLine("✓✓✓ VOICE RECOGNITION READY! ✓✓✓");
                            if (_permissionGranted)
                            {
                                Log.WriteLine("✓✓✓ MICROPHONE PERMISSION ALREADY GRANTED! ✓✓✓");
                            }
                            else
                            {
                                Log.WriteLine("⚠️ Microphone permission needed - will prompt on first use");
                            }
                            Log.WriteLine("════════════════════════════════════════════════");
                            
                            StatusChanged?.Invoke(this, "Ready");
                            return true;
                        }
                        else
                        {
                            throw new Exception("Navigation timeout");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"✗ Inner initialization error: {ex.GetType().Name}");
                        Log.WriteLine($"  Message: {ex.Message}");
                        return false;
                    }
                });

                _isInitialized = await result;
                _browserProcessFailed = false;
                _isInitializing = false;
                
                return _isInitialized;
            }
            catch (Exception ex)
            {
                Log.WriteLine("════════════════════════════════════════════════");
                Log.WriteLine("✗✗✗ VOICE INITIALIZATION FAILED ✗✗✗");
                Log.WriteLine($"Error: {ex.Message}");
                Log.WriteLine("════════════════════════════════════════════════");
                
                _isInitializing = false;
                _isInitialized = false;
                
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                return false;
            }
        }

        public bool HasMicrophonePermission() => _permissionGranted;
        public bool NeedsReinitialization() => _browserProcessFailed || !_isInitialized || _webView?.CoreWebView2 == null;

        public async Task<bool> EnsureReadyAsync()
        {
            if (_isDisposed || NeedsReinitialization())
            {
                return false;
            }

            if (!_permissionGranted)
            {
                var permissionGranted = await ShowPermissionPromptAsync();
                if (!permissionGranted)
                {
                    return false;
                }
            }

            return await PreWarmRecognitionAsync();
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                Log.WriteLine($"Browser → C#: {message}");
                
                if (message.StartsWith("TRANSCRIPT:"))
                {
                    var text = message.Substring("TRANSCRIPT:".Length);
                    Log.WriteLine($"✓ Recognized: '{text}'");
                    SpeechRecognized?.Invoke(this, text);
                }
                else if (message.StartsWith("STATUS:"))
                {
                    var status = message.Substring("STATUS:".Length);
                    Log.WriteLine($"Status: {status}");
                    StatusChanged?.Invoke(this, status);
                }
                else if (message.StartsWith("ERROR:"))
                {
                    var error = message.Substring("ERROR:".Length);
                    Log.WriteLine($"Error: {error}");
                    
                    // Only show permission window if it's a permission error
                    if (error.Contains("not-allowed") || error.Contains("permission"))
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            _ = ShowPermissionPromptAsync();
                        });
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, $"Error: {error}");
                    }
                }
                else if (message.StartsWith("PERMISSION:"))
                {
                    var permission = message.Substring("PERMISSION:".Length);
                    Log.WriteLine($"📋 Permission status: {permission}");
                    _permissionGranted = (permission == "granted");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Message handling error: {ex.Message}");
            }
        }

        private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            Log.WriteLine($"  🔐 Permission requested: {e.PermissionKind}");

            if (e.PermissionKind == CoreWebView2PermissionKind.Microphone)
            {
                Log.WriteLine("  ✅ Auto-granting microphone permission (already saved)");
                e.State = CoreWebView2PermissionState.Allow;
            }
        }

        private void OnBrowserProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            Log.WriteLine("════════════════════════════════════════════════");
            Log.WriteLine("✗ WEBVIEW2 BROWSER PROCESS FAILED");
            Log.WriteLine($"Kind: {e.ProcessFailedKind}");
            Log.WriteLine($"Reason: {e.Reason}");
            Log.WriteLine("════════════════════════════════════════════════");

            _browserProcessFailed = true;
            _isInitialized = false;
            _isInitializing = false;
            _isListening = false;

            StatusChanged?.Invoke(this, "Voice engine crashed");
        }

        private async Task<bool> ShowPermissionPromptAsync()
        {
            if (_webView == null)
            {
                return false;
            }

            Log.WriteLine("════════════════════════════════════════════════");
            Log.WriteLine("OPENING PERMISSION WINDOW");
            Log.WriteLine("════════════════════════════════════════════════");

            try
            {
                var permissionWindow = new MicrophonePermissionWindow();
                var result = permissionWindow.ShowDialog();

                if (result == true && permissionWindow.PermissionGranted)
                {
                    Log.WriteLine("✅ User granted microphone permission!");
                    _permissionGranted = true;
                    
                    StatusChanged?.Invoke(this, "Permission granted");
                    return true;
                }
                else
                {
                    Log.WriteLine("❌ User denied or cancelled permission");
                    StatusChanged?.Invoke(this, "Permission denied");
                    
                    InvisibleMessageBox.Show(
                        "❌ Microphone permission denied.\n\n" +
                        "Voice input will not work until you grant permission.\n\n" +
                        "Click the 🎤 button again to retry.",
                        "Permission Required"
                    );
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"❌ Permission window error: {ex.Message}");
                return false;
            }
            finally
            {
                Log.WriteLine("════════════════════════════════════════════════");
            }

            return false;
        }

        private async Task<bool> PreWarmRecognitionAsync()
        {
            if (NeedsReinitialization())
            {
                return false;
            }

            try
            {
                StatusChanged?.Invoke(this, "Warming microphone...");
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                    (async function() {
                        try {
                            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                            stream.getTracks().forEach(track => track.stop());

                            if (!recognition) {
                                initSpeechRecognition();
                            }

                            return 'ready';
                        } catch (error) {
                            return 'error:' + error.name + ':' + error.message;
                        }
                    })();
                ");

                var normalized = result?.Trim('"') ?? string.Empty;
                if (!normalized.Equals("ready", StringComparison.OrdinalIgnoreCase))
                {
                    Log.WriteLine($"⚠️ Voice pre-warm did not complete: {normalized}");
                    return false;
                }

                StatusChanged?.Invoke(this, "Ready");
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"⚠️ Voice pre-warm failed: {ex.Message}");
                return false;
            }
        }

        private string GetSpeechRecognitionHTML()
        {
            return @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <title>Speech Recognition</title>
            <style>
                body {
                    background: #1a1a1a;
                    color: #fff;
                    font-family: Arial, sans-serif;
                    padding: 20px;
                    margin: 0;
                }
                h1 { color: #4CAF50; }
                #status {
                    padding: 15px;
                    background: #333;
                    border-radius: 8px;
                    margin-top: 10px;
                    font-size: 14px;
                }
            </style>
        </head>
        <body>
            <h1>🎤 Speech Recognition</h1>
            <p id='status'>Ready</p>
            <script>
                console.log('Speech page loaded');
                console.log('Location:', window.location.href);
                console.log('isSecureContext:', window.isSecureContext);
                
                let recognition = null;
                let isListening = false;
                let shouldBeListening = false; // Track if we WANT to be listening

                function initSpeechRecognition() {
                    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
                    
                    if (!SpeechRecognition) {
                        window.chrome.webview.postMessage('ERROR:Not supported');
                        return false;
                    }

                    recognition = new SpeechRecognition();
                    
                    // CRITICAL: Enable continuous recognition
                    recognition.continuous = true;  // ← Changed from false
                    recognition.interimResults = true;  // ← Show interim results for better UX
                    recognition.maxAlternatives = 1;
                    recognition.lang = 'en-US';

                    recognition.onstart = () => {
                        console.log('✓ Recognition started');
                        isListening = true;
                        document.getElementById('status').textContent = '🎤 Listening continuously...';
                        window.chrome.webview.postMessage('STATUS:Listening - speak now!');
                    };

                    recognition.onresult = (event) => {
                        console.log('Got speech result');
                        
                        // Process all results
                        for (let i = event.resultIndex; i < event.results.length; i++) {
                            const result = event.results[i];
                            const transcript = result[0].transcript.trim();
                            
                            if (result.isFinal) {
                                // Final result - send to C#
                                console.log('✓ Final transcript:', transcript);
                                document.getElementById('status').textContent = '🎤 Heard: ' + transcript;
                                window.chrome.webview.postMessage('TRANSCRIPT:' + transcript);
                            } else {
                                // Interim result - show in status
                                console.log('... Interim:', transcript);
                                document.getElementById('status').textContent = '🎤 ... ' + transcript;
                            }
                        }
                    };

                    recognition.onerror = (event) => {
                        console.error('Speech error:', event.error);
                        
                        // Don't treat 'no-speech' as an error - just keep listening
                        if (event.error === 'no-speech') {
                            console.log('No speech detected, continuing to listen...');
                            document.getElementById('status').textContent = '🎤 Listening... (speak now)';
                            // Don't send error to C# - just keep listening
                            return;
                        }
                        
                        // For other errors, log but continue if user wants to keep listening
                        if (event.error === 'aborted') {
                            console.log('Recognition aborted');
                        } else {
                            console.error('Error:', event.error);
                            document.getElementById('status').textContent = 'Error: ' + event.error;
                            window.chrome.webview.postMessage('ERROR:' + event.error);
                        }
                    };

                    recognition.onend = () => {
                        console.log('Recognition ended');
                        isListening = false;
                        
                        // CRITICAL: If we should still be listening, restart immediately
                        if (shouldBeListening) {
                            console.log('Auto-restarting recognition...');
                            setTimeout(() => {
                                if (shouldBeListening) {
                                    try {
                                        recognition.start();
                                        console.log('✓ Restarted');
                                    } catch (err) {
                                        console.error('Restart failed:', err);
                                    }
                                }
                            }, 100); // Small delay to avoid race conditions
                        } else {
                            console.log('User stopped listening');
                            document.getElementById('status').textContent = 'Ready';
                            window.chrome.webview.postMessage('STATUS:Ready');
                        }
                    };

                    console.log('✓ Speech recognition initialized (continuous mode)');
                    window.chrome.webview.postMessage('STATUS:Ready');
                    return true;
                }

                function startListening() {
                    console.log('startListening() called');
                    
                    if (!recognition) {
                        console.log('Initializing recognition...');
                        if (!initSpeechRecognition()) {
                            return;
                        }
                    }

                    if (isListening) {
                        console.log('Already listening');
                        return;
                    }

                    shouldBeListening = true; // Mark that we want to keep listening
                    
                    try {
                        console.log('Starting continuous recognition...');
                        recognition.start();
                    } catch (err) {
                        console.error('Start error:', err);
                        
                        // If already started, that's fine
                        if (err.name === 'InvalidStateError') {
                            console.log('Already started, continuing...');
                        } else {
                            window.chrome.webview.postMessage('ERROR:' + err.message);
                        }
                    }
                }

                function stopListening() {
                    console.log('stopListening() called');
                    
                    shouldBeListening = false; // Mark that we want to stop
                    
                    if (!recognition || !isListening) {
                        console.log('Not listening, nothing to stop');
                        return;
                    }

                    try {
                        console.log('Stopping recognition...');
                        recognition.stop();
                        document.getElementById('status').textContent = 'Stopped';
                    } catch (err) {
                        console.error('Stop error:', err);
                    }
                }

                window.addEventListener('load', () => {
                    console.log('Initializing on page load...');
                    initSpeechRecognition();
                });
            </script>
        </body>
        </html>";
        }

        public async void StartListening()
        {
            Log.WriteLine("StartListening() called");
            
            if (NeedsReinitialization())
            {
                Log.WriteLine("Not initialized");
                return;
            }

            if (_isListening)
            {
                Log.WriteLine("Already listening");
                return;
            }

            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("startListening()");
                _isListening = true;
                Log.WriteLine("✓ Started listening");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Start failed: {ex.Message}");
            }
        }

        public async void StopListening()
        {
            if (!_isListening || NeedsReinitialization())
            {
                _isListening = false;
                return;
            }

            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("stopListening()");
                _isListening = false;
            }
            catch { }
        }

        public bool IsListening() => _isListening;
        public bool IsInitialized() => _isInitialized;

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isListening = false;
            _isInitializing = false;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_webView?.CoreWebView2 != null)
                {
                    if (_permissionRequestedHandler != null)
                    {
                        _webView.CoreWebView2.PermissionRequested -= _permissionRequestedHandler;
                        _permissionRequestedHandler = null;
                    }

                    if (_processFailedHandler != null)
                    {
                        _webView.CoreWebView2.ProcessFailed -= _processFailedHandler;
                        _processFailedHandler = null;
                    }

                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }

                if (_webView != null)
                {
                    if (_webView.Parent is System.Windows.Controls.Panel parentPanel)
                    {
                        parentPanel.Children.Remove(_webView);
                    }
                    else if (_hostContainer != null)
                    {
                        _hostContainer.Children.Remove(_webView);
                    }

                    _webView.Dispose();
                }
            });

            _webView = null;
            _hostContainer = null;
            _isInitialized = false;
            _browserProcessFailed = false;
        }
    }
}
