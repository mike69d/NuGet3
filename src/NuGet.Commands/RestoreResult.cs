﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using NuGet.LibraryModel;
using NuGet.Logging;
using NuGet.ProjectModel;

namespace NuGet.Commands
{
    public class RestoreResult
    {
        public bool Success { get; }

        public MSBuildRestoreResult MSBuild { get; }

        /// <summary>
        /// Gets the path that the lock file will be written to.
        /// </summary>
        public string LockFilePath { get; set; }

        /// <summary>
        /// Gets the resolved dependency graphs produced by the restore operation
        /// </summary>
        public IEnumerable<RestoreTargetGraph> RestoreGraphs { get; }

        public IEnumerable<CompatibilityCheckResult> CompatibilityCheckResults { get; }

        /// <summary>
        /// Gets a boolean indicating if the lock file will be re-written on <see cref="Commit"/>
        /// because the file needs to be re-locked.
        /// </summary>
        public bool RelockFile { get; }

        /// <summary>
        /// Gets the lock file that was generated during the restore or, in the case of a locked lock file,
        /// was used to determine the packages to install during the restore.
        /// </summary>
        public LockFile LockFile { get; }

        public RestoreResult(
            bool success, 
            IEnumerable<RestoreTargetGraph> restoreGraphs, 
            IEnumerable<CompatibilityCheckResult> compatibilityCheckResults, 
            LockFile lockFile, 
            string lockFilePath, 
            bool relockFile,
            MSBuildRestoreResult msbuild)
        {
            Success = success;
            RestoreGraphs = restoreGraphs;
            CompatibilityCheckResults = compatibilityCheckResults;
            LockFile = lockFile;
            LockFilePath = lockFilePath;
            MSBuild = msbuild;
        }

        /// <summary>
        /// Calculates the complete set of all packages installed by this operation
        /// </summary>
        /// <remarks>
        /// This requires quite a bit of iterating over the graph so the result should be cached
        /// </remarks>
        /// <returns>A set of libraries that were installed by this operation</returns>
        public ISet<LibraryIdentity> GetAllInstalled()
        {
            return new HashSet<LibraryIdentity>(RestoreGraphs.Where(g => !g.InConflict).SelectMany(g => g.Install).Distinct().Select(m => m.Library));
        }

        /// <summary>
        /// Calculates the complete set of all unresolved dependencies for this operation
        /// </summary>
        /// <remarks>
        /// This requires quite a bit of iterating over the graph so the result should be cached
        /// </remarks>
        /// <returns>A set of dependencies that were unable to be resolved by this operation</returns>
        public ISet<LibraryRange> GetAllUnresolved()
        {
            return new HashSet<LibraryRange>(RestoreGraphs.SelectMany(g => g.Unresolved).Distinct());
        }

        /// <summary>
        /// Commits the Lock File contained in <see cref="LockFile"/> and the MSBuild targets/props to
        /// the local file system.
        /// </summary>
        public void Commit(ILogger log)
        {
            // Write the lock file
            var lockFileFormat = new LockFileFormat();

            // Don't write the lock file if it is Locked AND we're not re-locking the file
            if (!LockFile.IsLocked || RelockFile)
            {
                lockFileFormat.Write(LockFilePath, LockFile);
            }

            MSBuild.Commit(log);
        }
    }
}
