using dvbapiNet.Oscam;
using System.Threading;
using Xunit;

namespace dvbapiNet.Tests
{
    public class ReconnectionStrategyTests
    {
        [Fact]
        public void Initial_CanRetryNow_IsTrue()
        {
            var s = new ReconnectionStrategy();
            Assert.True(s.CanRetryNow);
            Assert.Equal(0, s.AttemptCount);
        }

        [Fact]
        public void Backoff_Doubles_OnFailures()
        {
            var s = new ReconnectionStrategy(100, 1600, 2.0);
            s.OnConnectionFailed(); // 100 -> 200
            s.OnConnectionFailed(); // 200 -> 400
            s.OnConnectionFailed(); // 400 -> 800
            Assert.Equal(3, s.AttemptCount);
            Assert.Equal(800, s.CurrentDelayMs);
        }

        [Fact]
        public void Backoff_CapsAt_Max()
        {
            var s = new ReconnectionStrategy(100, 500, 2.0);
            for (int i = 0; i < 10; i++) s.OnConnectionFailed();
            Assert.Equal(500, s.CurrentDelayMs);
        }

        [Fact]
        public void Success_Resets_Backoff()
        {
            var s = new ReconnectionStrategy(100, 1600, 2.0);
            s.OnConnectionFailed();
            s.OnConnectionFailed();
            s.OnConnectionSuccess();
            Assert.Equal(0, s.AttemptCount);
            Assert.Equal(100, s.CurrentDelayMs);
            Assert.True(s.CanRetryNow);
        }

        [Fact]
        public void CanRetryNow_RespectsElapsedTime()
        {
            var s = new ReconnectionStrategy(150, 1600, 2.0);
            s.OnConnectionFailed();   // delay = 300, timer running
            Assert.False(s.CanRetryNow);
            Thread.Sleep(350);
            Assert.True(s.CanRetryNow);
        }
    }
}
