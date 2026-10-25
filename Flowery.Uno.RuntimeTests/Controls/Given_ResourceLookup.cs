using Flowery.Theming;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_ResourceLookup
    {
        [TestMethod]
        public void When_AKeyIsInANestedMergedOrThemeDictionary_TheLookupFindsIt()
        {
            // On Windows the lookup relies on TryGetValue searching nested dictionaries; elsewhere it searches them itself.
            var themed = new ResourceDictionary();
            themed.ThemeDictionaries["Default"] = new ResourceDictionary { ["ThemedKey"] = 2d };
            var nested = new ResourceDictionary { ["NestedKey"] = 1d };
            nested.MergedDictionaries.Add(themed);
            var middle = new ResourceDictionary();
            middle.MergedDictionaries.Add(nested);
            var root = new ResourceDictionary();
            root.MergedDictionaries.Add(middle);
            Assert.IsTrue(DaisyResourceLookup.TryGetResource(root, "NestedKey", out var value));
            Assert.AreEqual(1d, value);
            Assert.IsTrue(DaisyResourceLookup.TryGetResource(root, "ThemedKey", out value));
            Assert.AreEqual(2d, value);
            Assert.IsFalse(DaisyResourceLookup.TryGetResource(root, "MissingKey", out value));
            Assert.IsNull(value);
        }
    }
}
