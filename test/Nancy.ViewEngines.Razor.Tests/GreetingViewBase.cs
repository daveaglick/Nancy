namespace Nancy.ViewEngines.Razor.Tests
{
    public abstract class GreetingViewBase : NancyRazorPage
    {
        public string Greet()
        {
            return "Hi, Nancy!";
        }
    }
}