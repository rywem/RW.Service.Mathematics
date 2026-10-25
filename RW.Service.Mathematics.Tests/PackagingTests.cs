using RW.Service.Mathematics.Solvers;

namespace RW.Service.Mathematics.Tests
{
    public class PackagingTests
    {
        // The CAS stands alone (R-011 packaging): the framework, AngouriMath (with its PeterO "Numbers") and RW.Library.Mathematics, nothing app-side.
        [Fact]
        public void TheServiceReferencesOnlyTheFrameworkAngouriMathAndTheLibrary() =>
            Assert.All(typeof(AngouriMathService).Assembly.GetReferencedAssemblies(), a => Assert.True(
                a.Name!.StartsWith("System") || a.Name == "netstandard" || a.Name.StartsWith("AngouriMath") || a.Name == "Numbers" || a.Name == "RW.Library.Mathematics", a.Name));
    }
}
