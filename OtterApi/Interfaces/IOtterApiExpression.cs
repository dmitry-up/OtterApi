namespace OtterApi.Interfaces;

public interface IOtterApiExpression<T>
{
    T Build();
}