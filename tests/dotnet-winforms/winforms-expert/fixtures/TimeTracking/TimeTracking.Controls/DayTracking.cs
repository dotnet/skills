using System.Data.SqlClient;

namespace TimeTracking.Controls
{
    public partial class DayTracking : UserControl
    {
        public DayTracking()
        {
            InitializeComponent();
        }

        public SqlCommand CreateEmployeeEntriesCommand(
            SqlConnection connection,
            int employeeId)
        {
            var sql = "SELECT EntryId, StartedAt, EndedAt FROM TimeEntries WHERE EmployeeId = " + employeeId;
            return new SqlCommand(sql, connection);
        }
    }
}
