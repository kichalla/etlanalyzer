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
    class ProfileSample
    {
        public double ProcessStart_TimeStampRelativeMSec { get; set; }
        public double ClrStart_TimeStampRelativeMSec { get; set; }
        public double MainAppDllLoad_TimeStampRelativeMSec { get; set; }
        public double JitStart_TimeStampRelativeMSec { get; set; }
        public double AppStart_TimeStampRelativeMSec { get; set; }
        public double AppStartupStart_TimeStampRelativeMSec { get; set; }
        public double AppStartupStop_TimeStampRelativeMSec { get; set; }
        public double HostStarted_TimeStampRelativeMSec { get; set; }
        public double RequestStart_TimeStampRelativeMSec { get; set; }
        // Assembly load start?

        public TimeSpan ElapsedTime_BeforeClrStarts()
        {
            return TimeSpan.FromMilliseconds(ClrStart_TimeStampRelativeMSec - ProcessStart_TimeStampRelativeMSec);
        }

        public TimeSpan ElapsedTime_BeforeHostStarts()
        {
            return TimeSpan.FromMilliseconds(HostStarted_TimeStampRelativeMSec - ClrStart_TimeStampRelativeMSec);
        }

        public string ToCSVFormat()
        {
            var sb = new StringBuilder();
            var y = (int)ElapsedTime_BeforeClrStarts().TotalMilliseconds;
            sb.AppendFormat($"{y}, ");
            y = (int)ElapsedTime_BeforeHostStarts().TotalMilliseconds;
            sb.Append(y);
            return sb.ToString();
        }
    }

    class Program
    {
        static readonly List<ProfileSample> _profileSamples = new List<ProfileSample>();
        static ProfileSample _currentProfileSample = null;
        static double _totalJitTimeInMSec;
        static string _currentMethodBeingJitted;
        static double _currentMethodJittedTimeInMSec;

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
                    _currentProfileSample.AppStart_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }

                if (traceEvent.EventName == "HostStarted")
                {
                    _currentProfileSample.HostStarted_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }
            }

            // ConfigureServices is called before Configure
            if (traceEvent.ProviderName == "AspNetCoreHostingEventSource")
            {
                if (traceEvent.EventName == "StartConfigureApplicationServices")
                {
                    _currentProfileSample.AppStartupStart_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }

                if (traceEvent.EventName == "EndConfigureMiddlewarePipeline")
                {
                    _currentProfileSample.AppStartupStop_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }
            }
        }

        private static void Kernel_ProcessEnd(ProcessTraceData traceData)
        {
            if (traceData.ProcessName == "dotnet")
            {
                // reset the current profiling sample
                _currentProfileSample = null;
            }
        }
    }
}

