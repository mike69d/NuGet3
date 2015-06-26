// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGet.Packaging
{
    /// <summary>
    /// Writes the packages.config XML file to a stream
    /// </summary>
    public class PackagesConfigWriter : IDisposable
    {
        private const string PackagesNodeName = "packages";
        private const string PackageNodeName = "package";
        private const string IdAttributeName = "id";
        private const string VersionAttributeName = "version";
        private const string TargetFrameworkAttributeName = "targetFramework";
        private const string MinClientAttributeName = "minClientVersion";
        private const string developmentDependencyAttributeName = "developmentDependency";
        private const string allowedVersionsAttributeName = "allowedVersions";
        private const string RequireInstallAttributeName = "requireReinstallation";

        private readonly Stream _stream;
        private bool _disposed;
        private bool _closed;
        private NuGetVersion _minClientVersion;
        private IFrameworkNameProvider _frameworkMappings;
        private XDocument _xDocument;

        /// <summary>
        /// Create a packages.config writer
        /// </summary>
        /// <param name="stream">Stream to write the XML packages.config file into</param>
        public PackagesConfigWriter(Stream stream)
            : this(DefaultFrameworkNameProvider.Instance, stream)
        {
        }

        public PackagesConfigWriter(IFrameworkNameProvider frameworkMappings, Stream stream)
        {
            _stream = stream;
            _closed = false;
            _frameworkMappings = frameworkMappings;
            LoadOrCreateXDocument();
        }

        private void LoadOrCreateXDocument()
        {
            try
            {
                _xDocument = XDocument.Load(_stream, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                _xDocument = new XDocument();
            }
        }

        /// <summary>
        /// Write a minimum client version to packages.config
        /// </summary>
        /// <param name="version">Minumum version of the client required to parse and use this file.</param>
        public void WriteMinClientVersion(NuGetVersion version)
        {
            if (_minClientVersion != null)
            {
                throw new PackagingException(string.Format(CultureInfo.CurrentCulture, Strings.MinClientVersionAlreadyExist));
            }

            _minClientVersion = version;

            var packagesNode = EnsurePackagesNode();

            if (_minClientVersion != null)
            {
                var minClientVersionAttribute = new XAttribute(XName.Get(MinClientAttributeName), _minClientVersion.ToNormalizedString());
                packagesNode.Add(minClientVersionAttribute);
            }
        }

        /// <summary>
        /// Add a package entry
        /// </summary>
        /// <param name="packageId">Package Id</param>
        /// <param name="version">Package Version</param>
        /// <param name="targetFramework">Package targetFramework</param>
        public void WritePackageEntry(string packageId, NuGetVersion version, NuGetFramework targetFramework)
        {
            if (packageId == null)
            {
                throw new ArgumentNullException(nameof(packageId));
            }

            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            if (targetFramework == null)
            {
                throw new ArgumentNullException(nameof(targetFramework));
            }

            WritePackageEntry(new PackageIdentity(packageId, version), targetFramework);
        }

        /// <summary>
        /// Adds a basic package entry to the file
        /// </summary>
        public void WritePackageEntry(PackageIdentity identity, NuGetFramework targetFramework)
        {
            var entry = new PackageReference(identity, targetFramework);

            WritePackageEntry(entry);
        }

        /// <summary>
        /// Adds a package entry to the file
        /// </summary>
        /// <param name="entry">Package reference entry</param>
        public void WritePackageEntry(PackageReference entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (_disposed || _closed)
            {
                throw new PackagingException(string.Format(CultureInfo.CurrentCulture, Strings.UnableToAddEntry));
            }

            var packagesNode = EnsurePackagesNode();

            if (packagesNode.Descendants(PackageNodeName).Where(e => StringComparer.OrdinalIgnoreCase.Equals(e.FirstAttribute.Value, entry.PackageIdentity.Id)).Any())
            {
                throw new PackagingException(String.Format(CultureInfo.CurrentCulture, Strings.PackageEntryAlreadyExist, entry.PackageIdentity.Id));
            }
            else
            {
                // Append the entry to existing package nodes
                var node = CreateXElementForPackageEntry(entry);
                packagesNode.Add(node);

                // Sort the entries by package Id
                SortPackageNodes(packagesNode);
            }
        }

        /// <summary>
        /// Update a package entry to the file
        /// </summary>
        /// <param name="entry">Package reference entry</param>
        public void UpdatePackageEntry(PackageReference oldEntry, PackageReference newEntry)
        {
            if (oldEntry == null)
            {
                throw new ArgumentNullException(nameof(oldEntry));
            }

            if (newEntry == null)
            {
                throw new ArgumentNullException(nameof(newEntry));
            }

            if (_disposed || _closed)
            {
                throw new PackagingException(string.Format(CultureInfo.CurrentCulture, Strings.UnableToAddEntry));
            }

            var packagesNode = _xDocument.Descendants(PackagesNodeName).FirstOrDefault();

            // Check if package entry already exist on packages.config file
            var matchingNode = FindMatchingPackageNode(oldEntry, packagesNode);

            if (matchingNode == null)
            {
                throw new PackagingException(String.Format(CultureInfo.CurrentCulture, Strings.PackageEntryNotExist, oldEntry.PackageIdentity.Id, oldEntry.PackageIdentity.Version));
            }
            else
            {
                var newEntryNode = CreateXElementForPackageEntry(newEntry);
                matchingNode.ReplaceWith(newEntryNode);
            }
        }
        

        /// <summary>
        /// Remove a package entry
        /// </summary>
        /// <param name="packageId">Package Id</param>
        /// <param name="version">Package version</param>
        /// <param name="targetFramework">Package targetFramework</param>
        public void RemovePackageEntry(string packageId, NuGetVersion version, NuGetFramework targetFramework)
        {
            if (packageId == null)
            {
                throw new ArgumentNullException(nameof(packageId));
            }

            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            if (targetFramework == null)
            {
                throw new ArgumentNullException(nameof(targetFramework));
            }

            RemovePackageEntry(new PackageIdentity(packageId, version), targetFramework);
        }

        /// <summary>
        /// Remove a package identity to the file
        /// </summary>
        /// <param name="identity">Package identity</param>
        /// <param name="targetFramework">Package targetFramework</param>
        public void RemovePackageEntry(PackageIdentity identity, NuGetFramework targetFramework)
        {
            var entry = new PackageReference(identity, targetFramework);

            RemovePackageEntry(entry);
        }

        /// <summary>
        /// Removes a package entry from the file
        /// </summary>
        /// <param name="entry">Package reference entry</param>
        public void RemovePackageEntry(PackageReference entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (_disposed || _closed)
            {
                throw new PackagingException(string.Format(CultureInfo.CurrentCulture, Strings.UnableToAddEntry));
            }

            var packagesNode = _xDocument.Descendants(PackagesNodeName).FirstOrDefault();

            var matchingNode = FindMatchingPackageNode(entry, packagesNode);

            if (matchingNode == null)
            {
                throw new PackagingException(String.Format(CultureInfo.CurrentCulture, Strings.PackageEntryNotExist, entry.PackageIdentity.Id, entry.PackageIdentity.Version));
            }
            else
            {
                matchingNode.Remove();

                // Sort the package nodes after removal
                SortPackageNodes(packagesNode);
            }
        }

        private XElement EnsurePackagesNode()
        {
            var packagesNode = _xDocument.Descendants(PackagesNodeName).FirstOrDefault();

            if (packagesNode == null)
            {
                packagesNode = new XElement(XName.Get(PackagesNodeName));
                _xDocument.Add(packagesNode);
            }

            return packagesNode;
        }

        private XElement CreateXElementForPackageEntry(PackageReference entry)
        {
            var node = new XElement(XName.Get(PackageNodeName));

            node.Add(new XAttribute(XName.Get(IdAttributeName), entry.PackageIdentity.Id));
            node.Add(new XAttribute(XName.Get(VersionAttributeName), entry.PackageIdentity.Version));

            // map the framework to the short name
            // special frameworks such as any and unsupported will be ignored here
            if (entry.TargetFramework.IsSpecificFramework)
            {
                var frameworkShortName = entry.TargetFramework.GetShortFolderName(_frameworkMappings);

                if (!String.IsNullOrEmpty(frameworkShortName))
                {
                    node.Add(new XAttribute(XName.Get(TargetFrameworkAttributeName), frameworkShortName));
                }
            }

            if (entry.HasAllowedVersions)
            {
                node.Add(new XAttribute(XName.Get(allowedVersionsAttributeName), entry.AllowedVersions.ToString()));
            }

            if (entry.IsDevelopmentDependency)
            {
                node.Add(new XAttribute(XName.Get(developmentDependencyAttributeName), "true"));
            }

            if (entry.RequireReinstallation)
            {
                node.Add(new XAttribute(XName.Get(RequireInstallAttributeName), "true"));
            }

            return node;
        }

        private XElement FindMatchingPackageNode(PackageReference entry, XElement packagesNode)
        {
            var matchingNode = packagesNode?.Descendants(PackageNodeName)
                .Where(e => e.FirstAttribute.Value.Equals(entry.PackageIdentity.Id, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (matchingNode != null)
            {
                var versionAttribute = XName.Get(VersionAttributeName);
                NuGetVersion version;
                NuGetVersion.TryParse(matchingNode.Attributes(versionAttribute).FirstOrDefault().Value, out version);

                if (version.Equals(entry.PackageIdentity.Version))
                {
                    return matchingNode;
                }
            }

            return null;
        }

        private void SortPackageNodes(XElement packagesNode)
        {
            var newPackagesNode = new XElement(PackagesNodeName,
                from minClient in packagesNode.Attributes(MinClientAttributeName)
                select minClient,

                from package in packagesNode.Descendants(PackageNodeName)
                orderby package.FirstAttribute.Value
                select package);

            packagesNode.ReplaceWith(newPackagesNode);
        }

        private void WriteFile()
        {
            _xDocument.Save(_stream);
        }

        /// <summary>
        /// Write the file to the stream and close it to disallow further changes.
        /// </summary>
        public void Close()
        {
            if (!_closed)
            {
                _closed = true;

                WriteFile();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_closed)
            {
                Close();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Dispose(true);
            }
        }
    }
}
