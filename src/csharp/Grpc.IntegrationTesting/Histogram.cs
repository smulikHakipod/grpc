#region Copyright notice and license

// Copyright 2015, Google Inc.
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//     * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//     * Neither the name of Google Inc. nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Utils;
using NUnit.Framework;
using Grpc.Testing;

namespace Grpc.IntegrationTesting
{
    /// <summary>
    /// Basic implementation of histogram based on grpc/support/histogram.h.
    /// </summary>
    public class Histogram
    {
        readonly object myLock = new object();
        readonly double multiplier;
        readonly double oneOnLogMultiplier;
        readonly double maxPossible;
        readonly uint[] buckets;

        int count;
        double sum;
        double sumOfSquares;
        double min;
        double max;

        public Histogram(double resolution, double maxPossible)
        {
            GrpcPreconditions.CheckArgument(resolution > 0);
            GrpcPreconditions.CheckArgument(maxPossible > 0);
            this.maxPossible = maxPossible;
            this.multiplier = 1.0 + resolution;
            this.oneOnLogMultiplier = 1.0 / Math.Log(1.0 + resolution);
            this.buckets = new uint[FindBucket(maxPossible) + 1];

            ResetUnsafe();
        }

        public void AddObservation(double value)
        {
            lock (myLock)
            {
                AddObservationUnsafe(value);    
            }
        }

        /// <summary>
        /// Gets snapshot of stats and optionally resets the histogram.
        /// </summary>
        public HistogramData GetSnapshot(bool reset = false)
        {
            lock (myLock)
            {
                var histogramData = new HistogramData();
                GetSnapshotUnsafe(histogramData, reset);
                return histogramData;
            }
        }

        /// <summary>
        /// Merges snapshot of stats into <c>mergeTo</c> and optionally resets the histogram.
        /// </summary>
        public void GetSnapshot(HistogramData mergeTo, bool reset)
        {
            lock (myLock)
            {
                GetSnapshotUnsafe(mergeTo, reset);
            }
        }

        /// <summary>
        /// Finds bucket index to which given observation should go.
        /// </summary>
        private int FindBucket(double value)
        {
            value = Math.Max(value, 1.0);
            value = Math.Min(value, this.maxPossible);
            return (int)(Math.Log(value) * oneOnLogMultiplier);
        }

        private void AddObservationUnsafe(double value)
        {
            this.count++;
            this.sum += value;
            this.sumOfSquares += value * value;
            this.min = Math.Min(this.min, value);
            this.max = Math.Max(this.max, value);

            this.buckets[FindBucket(value)]++;
        }

        private void GetSnapshotUnsafe(HistogramData mergeTo, bool reset)
        {
            GrpcPreconditions.CheckArgument(mergeTo.Bucket.Count == 0 || mergeTo.Bucket.Count == buckets.Length);
            if (mergeTo.Count == 0)
            {
                mergeTo.MinSeen = min;
                mergeTo.MaxSeen = max;
            }
            else
            {
                mergeTo.MinSeen = Math.Min(mergeTo.MinSeen, min);
                mergeTo.MaxSeen = Math.Max(mergeTo.MaxSeen, max);
            }
            mergeTo.Count += count;
            mergeTo.Sum += sum;
            mergeTo.SumOfSquares += sumOfSquares;

            if (mergeTo.Bucket.Count == 0)
            {
                mergeTo.Bucket.AddRange(buckets);
            }
            else
            {
                for (int i = 0; i < buckets.Length; i++)
                {
                    mergeTo.Bucket[i] += buckets[i];
                }
            }

            if (reset)
            {
              ResetUnsafe();
            }
        }

        private void ResetUnsafe()
        {
            this.count = 0;
            this.sum = 0;
            this.sumOfSquares = 0;
            this.min = double.PositiveInfinity;
            this.max = double.NegativeInfinity;
            for (int i = 0; i < this.buckets.Length; i++)
            {
                this.buckets[i] = 0;
            }
        }
    }
}
