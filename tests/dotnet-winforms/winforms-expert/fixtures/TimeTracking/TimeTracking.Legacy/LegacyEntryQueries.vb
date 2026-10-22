Imports System.Data.SqlClient

Public NotInheritable Class LegacyEntryQueries
    Public Shared Function CreateNotesCommand(
        connection As SqlConnection,
        notesFilter As String
    ) As SqlCommand
        Dim command = connection.CreateCommand()
        command.CommandText = "SELECT EntryId, Notes FROM TimeEntries WHERE Notes = '" & notesFilter & "'"
        Return command
    End Function
End Class
