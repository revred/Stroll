namespace Stroll.Backtest.Tests.Core;

/// <summary>
/// Core trading enums shared across all test components
/// </summary>

public enum OptionType 
{ 
    Call, 
    Put 
}

public enum OrderSide 
{ 
    Buy, 
    Sell 
}

public enum OrderType 
{ 
    Market, 
    Limit 
}

public enum OptionStyle
{
    European,
    American
}