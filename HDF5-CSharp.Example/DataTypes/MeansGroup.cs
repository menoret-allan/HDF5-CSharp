﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HDF5CSharp.DataTypes;
using HDF5CSharp.Example.DataTypes.HDF5Store.DataTypes;
using Microsoft.Extensions.Logging;

namespace HDF5CSharp.Example.DataTypes
{
    public class MeansGroup : Hdf5BaseFile, IDisposable
    {
        [Hdf5Save(Hdf5Save.DoNotSave)] private ReaderWriterLockSlim LockSlim { get; }
        [Hdf5Save(Hdf5Save.DoNotSave)] private List<MeansFullECGEvent> MeansSamplesData { get; set; }
        [Hdf5Save(Hdf5Save.DoNotSave)] private bool record;
        [Hdf5Save(Hdf5Save.DoNotSave)] private long index;

        [Hdf5Save(Hdf5Save.DoNotSave)] private int BatchSizeInSeconds;
        [Hdf5Save(Hdf5Save.DoNotSave)] private int BatchSizeInSamples;
        [Hdf5Save(Hdf5Save.DoNotSave)] private PeriodicTimer MeansSystemEventWriter { get; set; }
        [Hdf5Save(Hdf5Save.DoNotSave)] private Task MeansSystemEventTaskWriter { get; set; }
        [Hdf5Save(Hdf5Save.DoNotSave)] private ChunkedCompound<MeansFullECGEvent> ChunkedMeansSystemEvents { get; set; }
        private CancellationTokenSource cts;


        public MeansGroup(long fileId, long groupRoot, ILogger logger) : base(fileId, groupRoot, "means", logger)
        {
            MeansSamplesData = new List<MeansFullECGEvent>();

            cts = new CancellationTokenSource();
            LockSlim = new ReaderWriterLockSlim();
            BatchSizeInSamples = 1;
            BatchSizeInSeconds = 4;
            index = 0;
            ChunkedMeansSystemEvents = new ChunkedCompound<MeansFullECGEvent>("ecg_means_events", GroupId);
            MeansSystemEventWriter = new PeriodicTimer(TimeSpan.FromSeconds(BatchSizeInSeconds));
            var token = cts.Token;
            MeansSystemEventTaskWriter = Task.Run(async () =>
            {
                while (await MeansSystemEventWriter.WaitForNextTickAsync())
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    if (MeansSamplesData.Count >= BatchSizeInSamples)
                    {
                        AppendSamples();
                    }
                }
            }, token);
        }

        private void AppendSamples()
        {

            if (MeansSamplesData.Any())
            {
                try
                {
                    LockSlim.EnterWriteLock();
                    ChunkedMeansSystemEvents.AppendOrCreateCompound(MeansSamplesData);
                    MeansSamplesData.Clear();
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"Error appending means events: {e.Message}");

                }
                finally
                {
                    LockSlim.ExitWriteLock();
                }

            }
            else
            {
                Logger.LogWarning("No means system events to write to H5 file");
            }
        }


        public void Dispose()
        {
            try
            {
                if (!Disposed)
                {
                    MeansSystemEventWriter.Dispose();
                    ChunkedMeansSystemEvents?.Dispose();
                    Hdf5.CloseGroup(GroupId);
                }

            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error closing Means group: {e.Message}");
            }
        }


        public void Enqueue(long timestamp, string data)
        {

            if (record)
            {
                try
                {
                    LockSlim.EnterWriteLock();
                    Interlocked.Increment(ref index);
                    var mse = new MeansFullECGEvent(index, timestamp, data);
                    MeansSamplesData.Add(mse);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"Error adding means full event: {e.Message}");
                }
                finally
                {
                    LockSlim.ExitWriteLock();
                }

            }
        }


        public Task WaitForDataWritten()
        {
            cts.Cancel(false);
            record = false;
            AppendSamples();
            return Task.CompletedTask;
        }

        public void StopRecording() => record = false;

        public void StartLogging() => record = true;

        public void EnqueueRange(List<(long timestamp, string data)> data)
        {
            if (record)
            {
                try
                {
                    LockSlim.EnterWriteLock();
                    List<MeansFullECGEvent> itms = new List<MeansFullECGEvent>(data.Count);
                    foreach ((long timestamp, string data) d in data)
                    {
                        Interlocked.Increment(ref index);
                        var mse = new MeansFullECGEvent(index, d.timestamp, d.data);
                        itms.Add(mse);
                    }
                    MeansSamplesData.AddRange(itms);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"Error adding means full event: {e.Message}");
                }
                finally
                {
                    LockSlim.ExitWriteLock();
                }

            }
        }
    }
}
