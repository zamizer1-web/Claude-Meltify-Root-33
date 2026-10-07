using Root33.Core;

namespace Root33.Core.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void Model_ClearingIdsCompareByValue()
        {
            Assert.Equal(new ClearingId(3), new ClearingId(3));
            Assert.NotEqual(new ClearingId(3), new ClearingId(4));
        }
    }
}
