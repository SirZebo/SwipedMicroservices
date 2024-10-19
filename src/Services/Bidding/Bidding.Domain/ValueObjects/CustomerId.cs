﻿namespace Bidding.Domain.ValueObjects;
public record CustomerId
{
    public Guid Value { get; }
    private CustomerId(Guid value)
        => Value = value;

    public static CustomerId Of(Guid value)
    {
        // Domain Validation
        ArgumentNullException.ThrowIfNull(value);
        if (value == Guid.Empty)
        {
            throw new DomainException("CustomerId cannot be empty");
        }

        return new CustomerId(value);
    }
}