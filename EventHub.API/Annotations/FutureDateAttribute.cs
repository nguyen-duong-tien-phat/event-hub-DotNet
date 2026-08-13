using System.ComponentModel.DataAnnotations;

namespace EventHub.Annotations;

public class FutureDateAttribute : ValidationAttribute {
    private readonly int _minDaysFromNow;

    public FutureDateAttribute(int minDaysFromNow = 1) {
        _minDaysFromNow = minDaysFromNow;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext context) {
        if (value is DateTime date) {
            var earliestAllowedDate = DateTime.UtcNow.Date.AddDays(_minDaysFromNow);
            if (date.Date < earliestAllowedDate) {
                return new ValidationResult(ErrorMessage ?? $"Date must be at least {_minDaysFromNow} day(s) from today");
            }
        }
        return ValidationResult.Success;
    }
}