using System.Data.SqlClient;

namespace TimeTracking
{
    public partial class FrmMain : Form
    {
        public FrmMain()
        {
            InitializeComponent();
        }

        internal SqlCommand CreateSearchCommand(
            SqlConnection connection,
            string userSearchText)
        {
            var command = connection.CreateCommand();
            command.CommandText = $$"""SELECT EntryId, EmployeeId, Notes FROM TimeEntries WHERE Notes LIKE '%{{userSearchText}}%'""";
            return command;
        }
    }
}
