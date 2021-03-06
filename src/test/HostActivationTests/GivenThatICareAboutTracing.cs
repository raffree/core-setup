// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using Xunit;
using Microsoft.Extensions.DependencyModel;

namespace Microsoft.DotNet.CoreSetup.Test.HostActivation.Tracing
{
    public class GivenThatICareAboutTracing : IClassFixture<GivenThatICareAboutTracing.SharedTestState>
    {
        private SharedTestState sharedTestState;

        // Trace messages currently expected for a passing app (somewhat randomly selected)
        private const String ExpectedVerboseMessage = "--- Begin breadcrumb write";
        private const String ExpectedInfoMessage = "Deps file:";
        private const String ExpectedBadPathMessage = "Unable to open COREHOST_TRACEFILE=";

        public GivenThatICareAboutTracing(GivenThatICareAboutTracing.SharedTestState fixture)
        {
            sharedTestState = fixture;
        }

        [Fact]
        public void TracingOff()
        {
            var fixture = sharedTestState.PreviouslyPublishedAndRestoredPortableAppProjectFixture.Copy();
            var dotnet = fixture.BuiltDotnet;
            var appDll = fixture.TestProject.AppDll;

            dotnet.Exec(appDll)
                .CaptureStdOut()
                .CaptureStdErr()
                .Execute()
                .Should()
                .Pass()
                .And
                .NotHaveStdErrContaining(ExpectedInfoMessage)
                .And
                .NotHaveStdErrContaining(ExpectedVerboseMessage);
        }

        [Fact]
        public void TracingOnDefault()
        {
            var fixture = sharedTestState.PreviouslyPublishedAndRestoredPortableAppProjectFixture.Copy();
            var dotnet = fixture.BuiltDotnet;
            var appDll = fixture.TestProject.AppDll;

            dotnet.Exec(appDll)
                .EnvironmentVariable("COREHOST_TRACE", "1")
                .CaptureStdOut()
                .CaptureStdErr()
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining("Hello World")
                .And
                .HaveStdErrContaining(ExpectedInfoMessage)
                .And
                .HaveStdErrContaining(ExpectedVerboseMessage);
        }

        [Fact]
        public void TracingOnVerbose()
        {
            var fixture = sharedTestState.PreviouslyPublishedAndRestoredPortableAppProjectFixture.Copy();
            var dotnet = fixture.BuiltDotnet;
            var appDll = fixture.TestProject.AppDll;

            dotnet.Exec(appDll)
                .EnvironmentVariable("COREHOST_TRACE", "1")
                .EnvironmentVariable("COREHOST_TRACE_VERBOSITY", "4")
                .CaptureStdOut()
                .CaptureStdErr()
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining("Hello World")
                .And
                .HaveStdErrContaining(ExpectedInfoMessage)
                .And
                .HaveStdErrContaining(ExpectedVerboseMessage);
        }

        [Fact]
        public void TracingOnInfo()
        {
            var fixture = sharedTestState.PreviouslyPublishedAndRestoredPortableAppProjectFixture.Copy();
            var dotnet = fixture.BuiltDotnet;
            var appDll = fixture.TestProject.AppDll;

            dotnet.Exec(appDll)
                .EnvironmentVariable("COREHOST_TRACE", "1")
                .EnvironmentVariable("COREHOST_TRACE_VERBOSITY", "3")
                .CaptureStdOut()
                .CaptureStdErr()
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining("Hello World")
                .And
                .HaveStdErrContaining(ExpectedInfoMessage)
                .And
                .NotHaveStdErrContaining(ExpectedVerboseMessage);
        }

        [Fact]
        public void TracingOnWarning()
        {
            var fixture = sharedTestState.PreviouslyPublishedAndRestoredPortableAppProjectFixture.Copy();
            var dotnet = fixture.BuiltDotnet;
            var appDll = fixture.TestProject.AppDll;

            dotnet.Exec(appDll)
                .EnvironmentVariable("COREHOST_TRACE", "1")
                .EnvironmentVariable("COREHOST_TRACE_VERBOSITY", "2")
                .CaptureStdOut()
                .CaptureStdErr()
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining("Hello World")
                .And
                .NotHaveStdErrContaining(ExpectedInfoMessage)
                .And
                .NotHaveStdErrContaining(ExpectedVerboseMessage);
        }

        [Fact]
        public void TracingOnToFileDefault()
        {
            var fixture = sharedTestState.PreviouslyPublishedAndRestoredPortableAppProjectFixture.Copy();
            var dotnet = fixture.BuiltDotnet;
            var appDll = fixture.TestProject.AppDll;

            dotnet.Exec(appDll)
                .EnvironmentVariable("COREHOST_TRACE", "1")
                .EnvironmentVariable("COREHOST_TRACEFILE", "TracingOnToFileDefault.log")
                .CaptureStdOut()
                .CaptureStdErr()
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining("Hello World")
                .And
                .NotHaveStdErrContaining(ExpectedInfoMessage)
                .And
                .NotHaveStdErrContaining(ExpectedVerboseMessage)
                .And
                .FileExists("TracingOnToFileDefault.log")
                .And
                .FileContains("TracingOnToFileDefault.log", ExpectedVerboseMessage);
        }

        [Fact]
        public void TracingOnToFileBadPathDefault()
        {
            var fixture = sharedTestState.PreviouslyPublishedAndRestoredPortableAppProjectFixture.Copy();
            var dotnet = fixture.BuiltDotnet;
            var appDll = fixture.TestProject.AppDll;

            dotnet.Exec(appDll)
                .EnvironmentVariable("COREHOST_TRACE", "1")
                .EnvironmentVariable("COREHOST_TRACEFILE", "badpath/TracingOnToFileBadPathDefault.log")
                .CaptureStdOut()
                .CaptureStdErr()
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining("Hello World")
                .And
                .HaveStdErrContaining(ExpectedInfoMessage)
                .And
                .HaveStdErrContaining(ExpectedVerboseMessage)
                .And
                .HaveStdErrContaining(ExpectedBadPathMessage);
        }

        public class SharedTestState : IDisposable
        {
            // Entry point projects
            public TestProjectFixture PreviouslyPublishedAndRestoredPortableAppProjectFixture { get; set; }

            public RepoDirectoriesProvider RepoDirectories { get; set; }

            public SharedTestState()
            {
                RepoDirectories = new RepoDirectoriesProvider();

                // Entry point projects
                PreviouslyPublishedAndRestoredPortableAppProjectFixture = new TestProjectFixture("PortableApp", RepoDirectories)
                    .EnsureRestored(RepoDirectories.CorehostPackages)
                    .PublishProject();
            }

            public void Dispose()
            {
                // Entry point projects
                PreviouslyPublishedAndRestoredPortableAppProjectFixture.Dispose();
            }
        }
    }
}
