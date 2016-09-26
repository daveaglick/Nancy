using Microsoft.AspNetCore.Mvc.Razor;

namespace Nancy.ViewEngines.Razor
{    
    /// <summary>
    /// Default base class for Nancy razor views
    /// </summary>
    public abstract class NancyRazorPage : NancyRazorPage<dynamic>
    {
    }

    public abstract class NancyRazorPage<T> : RazorPage<T>
    {
    }
}