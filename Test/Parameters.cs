using System.ComponentModel;
using System.Reflection;
using Engine;
using GuiFunctions;
using NUnit.Framework;
using Tasks;

namespace Test.GuiTests
{
    [TestFixture]
    [NonParallelizable]
    public class GuiGlobalParamsTests
    {
        private static string SettingsPath => GlobalParameters.DefaultGlobalParametersFilePath;

        [SetUp]
        public void SetUp()
        {
            // Ensure singleton reset before each test
            ResetSingleton();
        }

        [TearDown]
        public void TearDown()
        {
            // cleanup created settings file to avoid cross-test interference
            try
            {
                if (File.Exists(SettingsPath))
                {
                    File.Delete(SettingsPath);
                }
            }
            catch { }

            // Reset global analyte type so parallel digestion tests are not affected
            GlobalVariables.AnalyteType = AnalyteType.Peptide;

            ResetSingleton();
        }

        private static void ResetSingleton()
        {
            var vmType = typeof(GuiGlobalParamsViewModel);
            var instField = vmType.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            instField.SetValue(null, null);
        }

        [Test]
        public void SettingsFileExists_ReturnsTrueAfterSave()
        {
            var vm = GuiGlobalParamsViewModel.Instance;
            vm.Save();
            Assert.That(GuiGlobalParamsViewModel.SettingsFileExists(), Is.True);
            File.Delete(SettingsPath);
        }

        [Test]
        public void BooleanProperties_SetAndGet_TriggerPropertyChanged()
        {
            var vm = GuiGlobalParamsViewModel.Instance;
            var vmType = typeof(GuiGlobalParamsViewModel);
            var props = vmType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            int testedCount = 0;

            foreach (var prop in props)
            {
                // skip non-bool properties, non-read/write, and indexers
                if (prop.PropertyType != typeof(bool)) continue;
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                testedCount++;

                bool eventFired = false;
                PropertyChangedEventHandler handler = (s, e) =>
                {
                    if (e.PropertyName == prop.Name) eventFired = true;
                };

                vm.PropertyChanged += handler;
                try
                {
                    // read original value
                    bool before = (bool)prop.GetValue(vm);
                    // set to opposite
                    prop.SetValue(vm, !before);
                    // assert value changed
                    var afterObj = prop.GetValue(vm);
                    Assert.That(afterObj, Is.EqualTo((object)!before), $"Property {prop.Name} did not toggle as expected.");
                    // assert event fired
                    Assert.That(eventFired, Is.True, $"Changing {prop.Name} should raise PropertyChanged for that property.");
                    // restore original to avoid side-effects
                    prop.SetValue(vm, before);
                }
                finally
                {
                    vm.PropertyChanged -= handler;
                }
            }

            Assert.That(testedCount, Is.GreaterThan(0), "No boolean properties were found to test on GuiGlobalParamsViewModel.");
        }

        [Test]
        public void GuiGlobalParams_Equals_ChecksAllModeSwitchFields()
        {
            var a = new GlobalParameters();
            var b = a.Clone();

            Assert.That(a.Equals(b), Is.True, "Cloned params should be equal");

            // Change IsRnaMode
            b.IsRnaMode = !a.IsRnaMode;
            Assert.That(a.Equals(b), Is.False, "Should not be equal after changing IsRnaMode");
            b.IsRnaMode = a.IsRnaMode;

            Assert.That(a.Equals(b), Is.True, "Should be equal after reverting all changes");
        }

        [Test]
        public void GuiGlobalParams_Clone_CopiesAllModeSwitchFields()
        {
            var original = new GlobalParameters
            {
                IsRnaMode = true,
            };

            var clone = original.Clone();

            Assert.That(clone.IsRnaMode, Is.EqualTo(original.IsRnaMode));
        }

        [Test]
        public void IsRnaMode_SavesAndLoadsCorrectly()
        {
            var vm = GuiGlobalParamsViewModel.Instance;

            // Set RNA mode
            vm.IsRnaMode = true;
            vm.Save();

            // Reset singleton and reload
            ResetSingleton();
            var vm2 = GuiGlobalParamsViewModel.Instance;

            Assert.That(vm2.IsRnaMode, Is.True, "IsRnaMode should persist after save/load");
        }
    }
}
