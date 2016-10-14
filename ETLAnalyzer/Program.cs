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
        private static double _totalJitTimeInMSec;
        private static string _currentMethodBeingJitted;
        private static double _currentMethodJittedTimeInMSec;
        private static bool _enteredEntryPoint = false;

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
                eventSource.Kernel.ProcessStop += Kernel_ProcessStop; // to indicate that one sample data has finished
                eventSource.Clr.LoaderAssemblyLoad += Clr_LoaderAssemblyLoad;
                eventSource.Clr.LoaderAssemblyUnload += Clr_LoaderAssemblyUnload;
                eventSource.Dynamic.All += Dynamic_All;

                eventSource.Process();
            }

            using (var fileStream = File.Create($@"C:\Users\kichalla\Documents\profileinfo\{Guid.NewGuid().ToString()}.csv"))
            {
                using (var streamWriter = new StreamWriter(fileStream))
                {
                    streamWriter.WriteLine("ClrStartTime, EnteredEntryPoint, HostStarted, TotalJitTime, RequestProcessTime");
                    foreach (var sample in _profileSamples)
                    {
                        streamWriter.WriteLine(
                            sample.ElapsedTimeBeforeClrStarts.TotalMilliseconds + "," +
                            sample.ElapsedTime_BeforeEnteringAppEntryPoint.TotalMilliseconds + "," +
                            sample.ElapsedTime_BeforeHostStarts.TotalMilliseconds + "," +
                            sample.TotalTimeSpentInJitting.TotalMilliseconds + "," +
                            sample.RequestProcessingTime.TotalMilliseconds);
                    }
                }
            }
        }

        private static void Clr_LoaderAssemblyUnload(AssemblyLoadUnloadTraceData obj)
        {
            if (obj.FullyQualifiedAssemblyName.Contains("MusicStore"))
            {
                if (_currentProfileSample != null)
                {
                    _currentProfileSample.TotalTimeSpentInJitting = TimeSpan.FromMilliseconds(_totalJitTimeInMSec);
                }
                ResetData();
            }
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
                if (_enteredEntryPoint)
                {
                    var jitTimeOfCurrentMethod = (traceData.TimeStampRelativeMSec - _currentMethodJittedTimeInMSec);
                    Console.WriteLine(Math.Round(jitTimeOfCurrentMethod, 1) + " : " + methodName);
                    _totalJitTimeInMSec += Math.Round(jitTimeOfCurrentMethod, 1);
                }
            }
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

                if (traceEvent.EventName == "HostStarted")
                {
                    _currentProfileSample.HostStarted_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }
            }

            if (traceEvent.ProviderName == "AspNetCoreHostingEventSource")
            {
                if (traceEvent.EventName == "RequestStart")
                {
                    _currentProfileSample.RequestStart_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }

                if (traceEvent.EventName == "RequestEnd")
                {
                    _currentProfileSample.RequestStop_TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    return;
                }
            }
        }

        private static void Kernel_ProcessStop(ProcessTraceData traceData)
        {
            if (traceData.ProcessName == "dotnet")
            {
                // this is not firing
                Console.WriteLine("dotnet process stop");
            }
        }

        private static void ResetData()
        {
            // reset the current profiling sample
            _currentProfileSample = null;
            _totalJitTimeInMSec = 0;
            _currentMethodBeingJitted = null;
            _currentMethodJittedTimeInMSec = 0;
            _enteredEntryPoint = false;
        }
    }
}

