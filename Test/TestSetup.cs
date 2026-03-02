using NUnit.Framework;
using Engine;

namespace Test
{
    /// <summary>
    /// Assembly-wide test setup that ensures GlobalVariables is initialized
    /// before any tests run. This fixture runs once per test assembly.
    /// </summary>
    [SetUpFixture]
    public class TestSetup
    {
        [OneTimeSetUp]
        public void GlobalSetup()
        {
            // Force initialization of GlobalVariables static constructor
            // by accessing a public property
            var dataDir = GlobalVariables.DataDir;
            
            TestContext.WriteLine($"GlobalVariables initialized. DataDir: {dataDir}");
            TestContext.WriteLine($"ProteaseGuru Version: {GlobalVariables.ProteaseGuruVersion}");
            TestContext.WriteLine($"ElementsLocation: {GlobalVariables.ElementsLocation}");
            
            // Verify critical resources are loaded
            if (!GlobalVariables.AllModsKnown.Any())
            {
                TestContext.WriteLine("Warning: No modifications loaded");
            }
            else
            {
                TestContext.WriteLine($"Loaded {GlobalVariables.AllModsKnown.Count()} modifications");
            }
            
            if (GlobalVariables.ProteaseMods == null || !GlobalVariables.ProteaseMods.Any())
            {
                TestContext.WriteLine("Warning: No protease modifications loaded");
            }
            else
            {
                TestContext.WriteLine($"Loaded {GlobalVariables.ProteaseMods.Count} protease modifications");
            }
        }

        [OneTimeTearDown]
        public void GlobalTeardown()
        {
            // Cleanup if needed
            TestContext.WriteLine("Test assembly execution completed");
        }
    }
}
