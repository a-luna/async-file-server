namespace TplSocketServer
{
    public class Result
    {
        // TODO: Change error parameter to use Error type instead of string
        protected Result(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }

        public bool Failure => !Success;

        public static Result Fail(string message)
        {
            return new Result(false, message);
        }

        public static Result<T> Fail<T>(string message)
        {
            return new Result<T>(default(T), false, message);
        }

        public static Result Ok()
        {
            return new Result(true, string.Empty);
        }

        public static Result<T> Ok<T>(T value)
        {
            return new Result<T>(value, true, string.Empty);
        }

        public static Result Combine(params Result[] results)
        {
            foreach (Result result in results)
            {
                if (result.Failure)
                {
                    return result;
                }
            }

            return Ok();
        }
    }

    public class Result<T> : Result
    {

        protected internal Result(T value, bool success, string error)
            : base(success, error)
        {
            this.Value = value;
        }

        public T Value { get; }
    }
}
