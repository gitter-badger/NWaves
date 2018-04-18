﻿using NUnit.Framework;
using NWaves.Filters;
using NWaves.Signals;

namespace NWaves.Tests.FilterTests
{
    [TestFixture]
    public class TestMedianFilter
    {
        //private readonly MedianFilter _filter = new MedianFilter(5);

        [Test]
        public void TestMedianFiltering()
        {
            var filter = new MedianFilter(5);

            var input =    new[] { 2, 6, 5, 4, 0, 3, 5, 7, 9, 2, 0, 1f };
            var expected = new[] { 2, 4, 4, 4, 4, 4, 5, 5, 5, 2, 1, 0f };

            var filtered = filter.ApplyTo(new DiscreteSignal(1, input));

            Assert.That(filtered.Samples, Is.EqualTo(expected).Within(1e-10));
        }

        [Test]
        public void TestMedianFilteringDefault()
        {
            var filter = new MedianFilter();    // 9-point median filter

            var input =    new[] { 1, 6, 5, 2, 8, 1, 9, 5, 4, 2, 3, 4, 6, 7, 4f };
            var expected = new[] { 1, 1, 2, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 3, 3f };

            var filtered = filter.ApplyTo(new DiscreteSignal(1, input));

            Assert.That(filtered.Samples, Is.EqualTo(expected).Within(1e-10));
        }
    }
}
