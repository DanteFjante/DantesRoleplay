using System.Runtime.CompilerServices;

// The action-receipt seam is deliberately internal to DataAccess. Its transactional behaviour is
// safety-critical, so the test assembly is its only friend rather than exposing a second action API.
[assembly: InternalsVisibleTo("DantesRoleplay.Tests")]
