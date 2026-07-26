# Third-party notices

OPS Monitor's optional CPU sensor broker uses these unmodified packages:

- **LibreHardwareMonitorLib 0.9.6** — Mozilla Public License 2.0.
  Source and license: <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6>
- **BlackSharp.Core 1.0.7**, **DiskInfoToolkit 1.1.2**,
  **HidSharp 2.6.4**, **RAMSPDToolkit-NDD 1.4.2**, and Microsoft
  `System.*` support packages — transitive dependencies of
  LibreHardwareMonitorLib. Their package metadata and license links are
  available from <https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6>.

OPS Monitor does not redistribute PawnIO. CPU temperature setup requires the
separately installed, digitally signed official PawnIO edition from
<https://pawnio.eu/>. PawnIO remains independently installed when OPS Monitor
is removed because other hardware-monitoring applications may also use it.

OPS Monitor does not modify LibreHardwareMonitor. The exact upstream source
corresponding to the distributed library is available at the tag linked above.
The optional sensor broker communicates with the library through its public
API and is a separate OPS Monitor work.
