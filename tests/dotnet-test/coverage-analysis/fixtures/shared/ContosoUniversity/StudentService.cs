namespace ContosoUniversity.Services;

public class StudentService
{
    private readonly List<Student> _students = new();

    /// <summary>
    /// Enroll a student. Validates name, age, and duplicate checks.
    /// Complexity: moderate (multiple branches).
    /// </summary>
    public EnrollmentResult Enroll(string firstName, string lastName, int age)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return new EnrollmentResult(false, "Name is required.");

        if (age < 16 || age > 100)
            return new EnrollmentResult(false, "Age must be between 16 and 100.");

        var existing = _students.FirstOrDefault(s =>
            s.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase) &&
            s.LastName.Equals(lastName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return new EnrollmentResult(false, $"Student {firstName} {lastName} is already enrolled.");

        var student = new Student
        {
            Id = _students.Count + 1,
            FirstName = firstName,
            LastName = lastName,
            Age = age,
            EnrolledDate = DateTime.UtcNow
        };

        _students.Add(student);
        return new EnrollmentResult(true, "Enrolled successfully.", student.Id);
    }

    /// <summary>
    /// Calculate GPA from a list of grades. Handles edge cases.
    /// Complexity: high (loops, conditionals, null checks).
    /// </summary>
    public GpaResult CalculateGpa(int studentId, List<Grade>? grades)
    {
        var student = _students.FirstOrDefault(s => s.Id == studentId);
        if (student == null)
            return new GpaResult(false, 0, "Student not found.");

        if (grades == null || grades.Count == 0)
            return new GpaResult(true, 0, "No grades to calculate.");

        double totalPoints = 0;
        int totalCredits = 0;
        var warnings = new List<string>();

        foreach (var grade in grades)
        {
            if (grade.Credits <= 0)
            {
                warnings.Add($"Skipped invalid credit value for {grade.CourseName}.");
                continue;
            }

            double points = grade.LetterGrade?.ToUpperInvariant() switch
            {
                "A" => 4.0,
                "B" => 3.0,
                "C" => 2.0,
                "D" => 1.0,
                "F" => 0.0,
                _ => -1.0
            };

            if (points < 0)
            {
                warnings.Add($"Unknown grade '{grade.LetterGrade}' for {grade.CourseName}.");
                continue;
            }

            totalPoints += points * grade.Credits;
            totalCredits += grade.Credits;
        }

        if (totalCredits == 0)
            return new GpaResult(true, 0, "No valid grades after filtering.");

        double gpa = Math.Round(totalPoints / totalCredits, 2);

        string message = warnings.Count > 0
            ? $"GPA calculated with {warnings.Count} warning(s): {string.Join("; ", warnings)}"
            : "GPA calculated successfully.";

        return new GpaResult(true, gpa, message);
    }

    /// <summary>
    /// Simple lookup by ID. Complexity: low (1 branch).
    /// </summary>
    public Student? FindById(int id)
    {
        return _students.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>
    /// Search students by name. Complexity: low.
    /// </summary>
    public List<Student> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<Student>();

        return _students
            .Where(s => s.FirstName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        s.LastName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

public record EnrollmentResult(bool Success, string Message, int? StudentId = null);
public record GpaResult(bool Success, double Gpa, string Message);

public class Student
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int Age { get; set; }
    public DateTime EnrolledDate { get; set; }
}

public class Grade
{
    public string CourseName { get; set; } = string.Empty;
    public string? LetterGrade { get; set; }
    public int Credits { get; set; }
}
