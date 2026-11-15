using System.ComponentModel.DataAnnotations;
using EVChargeSwap.Common.Pagination;
using Xunit;

namespace EVChargeSwap.Tests.Unit;

public sealed class PaginationRequestTests
{
    [Fact]
    public void Defaults_select_the_first_twenty_rows()
    {
        var request = new PaginationRequest();
        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Equal(0, request.GetOffset());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void Invalid_or_overflowing_queries_fail_validation(int page, int pageSize)
    {
        var request = new PaginationRequest { Page = page, PageSize = pageSize };
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Fact]
    public void A_valid_page_has_the_correct_offset()
    {
        var request = new PaginationRequest { Page = 3, PageSize = 10 };
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
        Assert.Equal(20, request.GetOffset());
    }
}
