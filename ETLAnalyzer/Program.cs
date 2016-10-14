using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        public double ProcessStart_TimeStampRelativeMSec { get; set; }
        public double ClrStart_TimeStampRelativeMSec { get; set; }
        public double EnteringAppEntryPoint_TimeStampRelativeMSec { get; set; }
        public TimeSpan TotalTimeSpentInJitting { get; set; }
        public double HostStarted_TimeStampRelativeMSec { get; set; }
        public double RequestStart_TimeStampRelativeMSec { get; set; }
        public double RequestStop_TimeStampRelativeMSec { get; set; }

        public TimeSpan ElapsedTimeBeforeClrStarts
        {
            get
            {
                return TimeSpan.FromMilliseconds(ClrStart_TimeStampRelativeMSec - ProcessStart_TimeStampRelativeMSec);
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

        public TimeSpan RequestProcessingTimeInMsec
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
        private static double _totalJitTimeInMSec;
        private static string _currentMethodBeingJitted;
        private static double _currentMethodJittedTimeInMSec;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Invalid number of arguments. Provide full file path to the .etl file.");
                return;
            }

            using (var eventSource = new ETWTraceEventSource(args[0]))
            {
                eventSource.Kernel.ProcessStart += Kernel_ProcessStart;
                eventSource.Clr.RuntimeStart += Clr_RuntimeStart;
                eventSource.Clr.MethodJittingStarted += Clr_MethodJittingStarted;
                eventSource.Clr.MethodLoadVerbose += Clr_MethodLoadVerbose; // When method is loaded inidcating jitting has finished
                eventSource.Kernel.ProcessStop += Kernel_ProcessEnd; // to indicate that one sample data has finished
                eventSource.Clr.LoaderAssemblyLoad += Clr_LoaderAssemblyLoad;
                eventSource.Dynamic.All += Dynamic_All;

                eventSource.Process();
            }

            //using (var f = File.Create(@"c:\testing\profile.csv"))
            //{
            //    using (var streamWriter = new StreamWriter(f))
            //    {
            //        streamWriter.WriteLine("ProcessStart, ClrStart, HostStarted");
            //        foreach (var sample in samples)
            //        {
            //            streamWriter.WriteLine(sample.ToCSVFormat());
            //        }
            //    }
            //}

            //Console.WriteLine("Total JIT timein MSec: " + _totalJitTimeInMSec);
        }

        private static void Kernel_ProcessStart(ProcessTraceData traceData)
        {
            if (traceData.ProcessName == "dotnet")
            {
                _currentProfileSample = new ProfileSample();
                _profileSamples.Add(_currentProfileSample);
                _currentProfileSample.ProcessStart_TimeStampRelativeMSec = traceData.TimeStampRelativeMSec;
            }
        }

        private static void Clr_RuntimeStart(RuntimeInformationTraceData traceData)
        {
            _currentProfileSample.ClrStart_TimeStampRelativeMSec = traceData.TimeStampRelativeMSec;
        }

        private static void Clr_MethodJittingStarted(MethodJittingStartedTraceData traceData)
        {
            _currentMethodBeingJitted = traceData.MethodNamespace + "." + traceData.MethodName;
            _currentMethodJittedTimeInMSec = traceData.TimeStampRelativeMSec;
        }

        private static void Clr_LoaderAssemblyLoad(AssemblyLoadUnloadTraceData traceData)
        {

        }

        private static void Clr_MethodLoadVerbose(MethodLoadUnloadVerboseTraceData traceData)
        {
            var methodName = traceData.MethodNamespace + "." + traceData.MethodName;
            if (methodName == _currentMethodBeingJitted)
            {
                var jitTimeOfCurrentMethod = (traceData.TimeStampRelativeMSec - _currentMethodJittedTimeInMSec);
                _totalJitTimeInMSec += jitTimeOfCurrentMethod;
            }
        }

        private static void Dynamic_All(TraceEvent traceEvent)
        {
            if (traceEvent.ProviderName == "MusicStoreEventSource")
            {
                if (traceEvent.EventName == "EnteringMain")
                {
                    _currentProfileSample.EnteringAppEntryPoint_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }

                if (traceEvent.EventName == "HostStarted")
                {
                    _currentProfileSample.HostStarted_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }
            }
        }

        private static void Kernel_ProcessEnd(ProcessTraceData traceData)
        {
            if (traceData.ProcessName == "dotnet")
            {
                _currentProfileSample.TotalTimeSpentInJitting = TimeSpan.FromMilliseconds(_totalJitTimeInMSec);

                // reset the current profiling sample
                _currentProfileSample = null;
            }
        }
    }
}

