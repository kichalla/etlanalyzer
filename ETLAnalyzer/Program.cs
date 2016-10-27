using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace ETLAnalyzer
{
    internal class ProfileSample
    {
        public double PreDotnetProcessStart_TimeStampRelativeMSec { get; set; }
        public double DotnetProcessStart_TimeStampRelativeMSec { get; set; }
        public double ClrStart_TimeStampRelativeMSec { get; set; }
        public double EnteringAppEntryPoint_TimeStampRelativeMSec { get; set; }
        public double HostStarted_TimeStampRelativeMSec { get; set; }
        public double RequestStart_TimeStampRelativeMSec { get; set; }
        public double RequestStop_TimeStampRelativeMSec { get; set; }


        public TimeSpan ElapsedTimeBeforeDotnetProcessStarts
        {
            get
            {
                return TimeSpan.FromMilliseconds(DotnetProcessStart_TimeStampRelativeMSec - PreDotnetProcessStart_TimeStampRelativeMSec);
            }
        }

        public TimeSpan ElapsedTimeBeforeClrStarts
        {
            get
            {
                return TimeSpan.FromMilliseconds(ClrStart_TimeStampRelativeMSec - DotnetProcessStart_TimeStampRelativeMSec);
            }
        }

        public TimeSpan ElapsedTime_BeforeEnteringAppEntryPoint
        {
            get
            {
                return TimeSpan.FromMilliseconds(EnteringAppEntryPoint_TimeStampRelativeMSec - ClrStart_TimeStampRelativeMSec);
            }
        }

        public TimeSpan ElapsedTime_BeforeHostStarts
        {
            get
            {
                return TimeSpan.FromMilliseconds(HostStarted_TimeStampRelativeMSec - EnteringAppEntryPoint_TimeStampRelativeMSec);
            }
        }

        public TimeSpan RequestProcessingTime
        {
            get
            {
                return TimeSpan.FromMilliseconds(RequestStop_TimeStampRelativeMSec - RequestStart_TimeStampRelativeMSec);
            }
        }
    }

    internal class Program
    {
        private static readonly List<ProfileSample> _profileSamples = new List<ProfileSample>();
        private static ProfileSample _currentProfileSample = null;
        private static bool _enteredEntryPoint = false;
        
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Invalid number of arguments.");
                Console.WriteLine("Usage: <Full-path-to-ETL-file> <Full-path-to-csv-file-to-create>");
                return;
            }

            using (var eventSource = new ETWTraceEventSource(args[0]))
            {
                eventSource.Kernel.ProcessStart += Kernel_ProcessStart;
                eventSource.Clr.RuntimeStart += Clr_RuntimeStart;
                eventSource.Kernel.ProcessStop += Kernel_ProcessStop; // to indicate that one sample data has finished
                eventSource.Dynamic.All += Dynamic_All;

                eventSource.Process();
            }

            using (var fileStream = File.Create(args[1]))
            {
                using (var streamWriter = new StreamWriter(fileStream))
                {
                    streamWriter.WriteLine("DotnetStartTime, ClrStartTime, EnteredEntryPoint, HostStarted, RequestProcessTime");
                    foreach (var sample in _profileSamples)
                    {
                        streamWriter.WriteLine(
                            sample.ElapsedTimeBeforeDotnetProcessStarts.TotalMilliseconds + "," +
                            sample.ElapsedTimeBeforeClrStarts.TotalMilliseconds + "," +
                            sample.ElapsedTime_BeforeEnteringAppEntryPoint.TotalMilliseconds + "," +
                            sample.ElapsedTime_BeforeHostStarts.TotalMilliseconds + "," +
                            sample.RequestProcessingTime.TotalMilliseconds);
                    }
                }
            }
        }

        private static void Kernel_ProcessStart(ProcessTraceData traceData)
        {
            if (traceData.ImageFileName == "w3wp.exe")
            {
                Console.WriteLine("Id of w3sp.exe: " + traceData.ProcessID);
                _currentProfileSample = new ProfileSample();
                _profileSamples.Add(_currentProfileSample);
                _currentProfileSample.PreDotnetProcessStart_TimeStampRelativeMSec = traceData.TimeStampRelativeMSec;
            }

            if (traceData.ImageFileName == "dotnet.exe")
            {
                Console.WriteLine("Parent Id of dotnet.exe: " + traceData.ParentID);
                Console.WriteLine(traceData.CommandLine);
                Console.WriteLine();
                _currentProfileSample.DotnetProcessStart_TimeStampRelativeMSec = traceData.TimeStampRelativeMSec;
            }
        }

        private static void Clr_RuntimeStart(RuntimeInformationTraceData traceData)
        {
            _currentProfileSample.ClrStart_TimeStampRelativeMSec = traceData.TimeStampRelativeMSec;
        }

        private static void Dynamic_All(TraceEvent traceEvent)
        {
            if (traceEvent.ProviderName == "MusicStoreEventSource")
            {
                if (traceEvent.EventName == "EnteringMain")
                {
                    _enteredEntryPoint = true;
                    _currentProfileSample.EnteringAppEntryPoint_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }
            }

            if (traceEvent.ProviderName == "AspNetCoreHostingEventSource")
            {
                if (traceEvent.EventName == "Request/Start")
                {
                    _currentProfileSample.RequestStart_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }

                if (traceEvent.EventName == "RequestEnd")
                {
                    _currentProfileSample.RequestStop_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }

                if (traceEvent.EventName == "HostStartEnd")
                {
                    _currentProfileSample.HostStarted_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }
            }
        }

        private static void Kernel_ProcessStop(ProcessTraceData traceData)
        {
            // NOTE: ProcessName property is empty so using ImageFileName
            if (traceData.ImageFileName == "w3wp.exe")
            {
                ResetData();
            }
        }

        private static void ResetData()
        {
            // reset the current profiling sample
            _currentProfileSample = null;
            _enteredEntryPoint = false;
        }
    }
}

