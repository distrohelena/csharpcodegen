using System.Security.Cryptography;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Locks the browser adapter to the tag-size range exposed by the target .NET runtime.</summary>
    public sealed class AesGcmTagSizeContractTests {
        /// <summary>The .NET 9 AesGcm API accepts every integer tag length from 12 through 16 bytes.</summary>
        [Fact]
        public void NativeAesGcmAcceptsOnlyTheTwelveThroughSixteenByteTagRange() {
            Assert.Equal(12, AesGcm.TagByteSizes.MinSize);
            Assert.Equal(16, AesGcm.TagByteSizes.MaxSize);
            Assert.Equal(1, AesGcm.TagByteSizes.SkipSize);
        }
    }
}
