using System;

namespace SecureOverlay.Services
{
    /// <summary>
    /// Simulates API errors for testing rotation logic
    /// </summary>
    public static class ErrorSimulator
    {
        private static Random _random = new Random();
        private static int _requestCount = 0;
        
        public static bool IsDebugModeEnabled { get; set; } = false;
        public static string SimulationMode { get; set; } = "None";
        
        /// <summary>
        /// Check if we should simulate an error for this request
        /// </summary>
        public static (bool shouldError, string errorMessage) ShouldSimulateError()
        {
            if (!IsDebugModeEnabled || SimulationMode == "None")
                return (false, "");
            
            _requestCount++;
            
            switch (SimulationMode)
            {
                case "429":
                    // Simulate 429 error every time
                    Log.WriteLine($"🧪 TEST MODE: Simulating 429 error (request #{_requestCount})");
                    return (true, "Error: 429 - Rate limit exceeded (SIMULATED FOR TESTING)");
                
                case "Timeout":
                    // Simulate timeout error every time
                    Log.WriteLine($"🧪 TEST MODE: Simulating timeout error (request #{_requestCount})");
                    return (true, "Error: Request timeout (SIMULATED FOR TESTING)");
                
                case "Random":
                    // 50% chance of 429, 30% chance of timeout, 20% success
                    var roll = _random.Next(100);
                    if (roll < 50)
                    {
                        Log.WriteLine($"🧪 TEST MODE: Random 429 error (request #{_requestCount})");
                        return (true, "Error: 429 - Rate limit exceeded (SIMULATED)");
                    }
                    else if (roll < 80)
                    {
                        Log.WriteLine($"🧪 TEST MODE: Random timeout error (request #{_requestCount})");
                        return (true, "Error: Request timeout (SIMULATED)");
                    }
                    else
                    {
                        Log.WriteLine($"🧪 TEST MODE: Allowing request through (request #{_requestCount})");
                        return (false, "");
                    }
                
                case "AlternatingKeys":
                    // Simulate 429 every other request to test key switching
                    if (_requestCount % 2 == 1)
                    {
                        Log.WriteLine($"🧪 TEST MODE: Alternating 429 (request #{_requestCount})");
                        return (true, "Error: 429 - Rate limit (ALTERNATING TEST)");
                    }
                    return (false, "");
                
                case "FirstTwoFail":
                    // First 2 requests fail with 429, rest succeed (tests key+model switch)
                    if (_requestCount <= 2)
                    {
                        Log.WriteLine($"🧪 TEST MODE: First-two-fail 429 (request #{_requestCount})");
                        return (true, "Error: 429 - Rate limit (FIRST TWO FAIL TEST)");
                    }
                    return (false, "");
                
                default:
                    return (false, "");
            }
        }
        
        public static void Reset()
        {
            _requestCount = 0;
            Log.WriteLine("🧪 TEST MODE: Request counter reset");
        }
        
        public static string GetStatus()
        {
            return $"Debug Mode: {(IsDebugModeEnabled ? "ON" : "OFF")} | " +
                   $"Simulation: {SimulationMode} | " +
                   $"Requests: {_requestCount}";
        }
    }
}
