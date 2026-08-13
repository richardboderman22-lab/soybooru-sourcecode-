using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.DTOs.Admin;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class TagRelationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ILogger<TagRelationService>> _mockLogger;
    private readonly Mock<ILogger<TagService>> _mockTagServiceLogger;
    private readonly TagService _tagService;
    private readonly TagRelationService _sut;

    public TagRelationServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _mockLogger = new Mock<ILogger<TagRelationService>>();
        _mockTagServiceLogger = new Mock<ILogger<TagService>>();
        _tagService = new TagService(_context, _mockTagServiceLogger.Object);
        _sut = new TagRelationService(_context, _tagService, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    #region Alias Resolution Tests

    [Fact]
    public async Task ResolveAliasAsync_WithNoAlias_ReturnsOriginalTag()
    {
        // Arrange
        var tag = CreateTag("cat");
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ResolveAliasAsync(tag);

        // Assert
        result.Id.Should().Be(tag.Id);
        result.Name.Should().Be("cat");
    }

    [Fact]
    public async Task ResolveAliasAsync_WithSimpleAlias_ReturnsTargetTag()
    {
        // Arrange
        var kittyTag = CreateTag("kitty");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(kittyTag, catTag);

        var alias = new TagAlias
        {
            AliasTagId = kittyTag.Id,
            AliasTag = kittyTag,
            TargetTagId = catTag.Id,
            TargetTag = catTag,
            IsActive = true
        };
        await _context.BooruTagAliases.AddAsync(alias);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ResolveAliasAsync(kittyTag);

        // Assert
        result.Id.Should().Be(catTag.Id);
        result.Name.Should().Be("cat");
    }

    [Fact]
    public async Task ResolveAliasAsync_WithChainedAliases_ReturnsCanonicalTag()
    {
        // Arrange: kitten -> kitty -> cat
        var kittenTag = CreateTag("kitten");
        var kittyTag = CreateTag("kitty");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(kittenTag, kittyTag, catTag);

        var alias1 = new TagAlias
        {
            AliasTagId = kittenTag.Id,
            AliasTag = kittenTag,
            TargetTagId = kittyTag.Id,
            TargetTag = kittyTag,
            IsActive = true
        };
        var alias2 = new TagAlias
        {
            AliasTagId = kittyTag.Id,
            AliasTag = kittyTag,
            TargetTagId = catTag.Id,
            TargetTag = catTag,
            IsActive = true
        };
        await _context.BooruTagAliases.AddRangeAsync(alias1, alias2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ResolveAliasAsync(kittenTag);

        // Assert
        result.Id.Should().Be(catTag.Id);
        result.Name.Should().Be("cat");
    }

    [Fact]
    public async Task ResolveAliasAsync_WithInactiveAlias_ReturnsOriginalTag()
    {
        // Arrange
        var kittyTag = CreateTag("kitty");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(kittyTag, catTag);

        var alias = new TagAlias
        {
            AliasTagId = kittyTag.Id,
            AliasTag = kittyTag,
            TargetTagId = catTag.Id,
            TargetTag = catTag,
            IsActive = false // Inactive
        };
        await _context.BooruTagAliases.AddAsync(alias);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ResolveAliasAsync(kittyTag);

        // Assert
        result.Id.Should().Be(kittyTag.Id); // Should return original since alias is inactive
    }

    [Fact]
    public async Task ResolveAliasesAsync_WithMultipleTags_ResolvesAll()
    {
        // Arrange
        var kittyTag = CreateTag("kitty");
        var catTag = CreateTag("cat");
        var dogTag = CreateTag("dog");
        await _context.BooruTags.AddRangeAsync(kittyTag, catTag, dogTag);

        var alias = new TagAlias
        {
            AliasTagId = kittyTag.Id,
            AliasTag = kittyTag,
            TargetTagId = catTag.Id,
            TargetTag = catTag,
            IsActive = true
        };
        await _context.BooruTagAliases.AddAsync(alias);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _sut.ResolveAliasesAsync(new[] { kittyTag, dogTag })).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "cat");
        result.Should().Contain(t => t.Name == "dog");
        result.Should().NotContain(t => t.Name == "kitty");
    }

    [Fact]
    public async Task ResolveAliasesAsync_WithDuplicateResolutions_DeduplicatesResults()
    {
        // Arrange: both kitty and kitten alias to cat
        var kittyTag = CreateTag("kitty");
        var kittenTag = CreateTag("kitten");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(kittyTag, kittenTag, catTag);

        var alias1 = new TagAlias
        {
            AliasTagId = kittyTag.Id,
            AliasTag = kittyTag,
            TargetTagId = catTag.Id,
            TargetTag = catTag,
            IsActive = true
        };
        var alias2 = new TagAlias
        {
            AliasTagId = kittenTag.Id,
            AliasTag = kittenTag,
            TargetTagId = catTag.Id,
            TargetTag = catTag,
            IsActive = true
        };
        await _context.BooruTagAliases.AddRangeAsync(alias1, alias2);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _sut.ResolveAliasesAsync(new[] { kittyTag, kittenTag })).ToList();

        // Assert
        result.Should().HaveCount(1); // Both resolve to cat, so only one result
        result.First().Name.Should().Be("cat");
    }

    #endregion

    #region Implication Tests

    [Fact]
    public async Task GetImpliedTagsAsync_WithNoImplications_ReturnsEmpty()
    {
        // Arrange
        var tag = CreateTag("cat");
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetImpliedTagsAsync(tag);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetImpliedTagsAsync_WithSimpleImplication_ReturnsImpliedTag()
    {
        // Arrange: tabby_cat -> cat
        var tabbyTag = CreateTag("tabby_cat");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(tabbyTag, catTag);

        var implication = new TagImplication
        {
            AntecedentTagId = tabbyTag.Id,
            AntecedentTag = tabbyTag,
            ConsequentTagId = catTag.Id,
            ConsequentTag = catTag,
            IsActive = true
        };
        await _context.BooruTagImplications.AddAsync(implication);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _sut.GetImpliedTagsAsync(tabbyTag)).ToList();

        // Assert
        result.Should().HaveCount(1);
        result.First().Name.Should().Be("cat");
    }

    [Fact]
    public async Task GetImpliedTagsAsync_WithTransitiveImplications_ReturnsAllImpliedTags()
    {
        // Arrange: tabby_cat -> cat -> mammal
        var tabbyTag = CreateTag("tabby_cat");
        var catTag = CreateTag("cat");
        var mammalTag = CreateTag("mammal");
        await _context.BooruTags.AddRangeAsync(tabbyTag, catTag, mammalTag);

        var implication1 = new TagImplication
        {
            AntecedentTagId = tabbyTag.Id,
            AntecedentTag = tabbyTag,
            ConsequentTagId = catTag.Id,
            ConsequentTag = catTag,
            IsActive = true
        };
        var implication2 = new TagImplication
        {
            AntecedentTagId = catTag.Id,
            AntecedentTag = catTag,
            ConsequentTagId = mammalTag.Id,
            ConsequentTag = mammalTag,
            IsActive = true
        };
        await _context.BooruTagImplications.AddRangeAsync(implication1, implication2);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _sut.GetImpliedTagsAsync(tabbyTag)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "cat");
        result.Should().Contain(t => t.Name == "mammal");
    }

    [Fact]
    public async Task GetImpliedTagsAsync_WithMultipleImplications_ReturnsAllImpliedTags()
    {
        // Arrange: tabby_cat -> cat, tabby_cat -> striped
        var tabbyTag = CreateTag("tabby_cat");
        var catTag = CreateTag("cat");
        var stripedTag = CreateTag("striped");
        await _context.BooruTags.AddRangeAsync(tabbyTag, catTag, stripedTag);

        var implication1 = new TagImplication
        {
            AntecedentTagId = tabbyTag.Id,
            AntecedentTag = tabbyTag,
            ConsequentTagId = catTag.Id,
            ConsequentTag = catTag,
            IsActive = true
        };
        var implication2 = new TagImplication
        {
            AntecedentTagId = tabbyTag.Id,
            AntecedentTag = tabbyTag,
            ConsequentTagId = stripedTag.Id,
            ConsequentTag = stripedTag,
            IsActive = true
        };
        await _context.BooruTagImplications.AddRangeAsync(implication1, implication2);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _sut.GetImpliedTagsAsync(tabbyTag)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "cat");
        result.Should().Contain(t => t.Name == "striped");
    }

    [Fact]
    public async Task GetImpliedTagsAsync_WithInactiveImplication_ExcludesInactive()
    {
        // Arrange
        var tabbyTag = CreateTag("tabby_cat");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(tabbyTag, catTag);

        var implication = new TagImplication
        {
            AntecedentTagId = tabbyTag.Id,
            AntecedentTag = tabbyTag,
            ConsequentTagId = catTag.Id,
            ConsequentTag = catTag,
            IsActive = false // Inactive
        };
        await _context.BooruTagImplications.AddAsync(implication);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetImpliedTagsAsync(tabbyTag);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllImpliedTagsAsync_WithMultipleTags_ReturnsAllImpliedTags()
    {
        // Arrange: tabby_cat -> cat, golden_retriever -> dog
        var tabbyTag = CreateTag("tabby_cat");
        var catTag = CreateTag("cat");
        var goldenTag = CreateTag("golden_retriever");
        var dogTag = CreateTag("dog");
        await _context.BooruTags.AddRangeAsync(tabbyTag, catTag, goldenTag, dogTag);

        var implication1 = new TagImplication
        {
            AntecedentTagId = tabbyTag.Id,
            AntecedentTag = tabbyTag,
            ConsequentTagId = catTag.Id,
            ConsequentTag = catTag,
            IsActive = true
        };
        var implication2 = new TagImplication
        {
            AntecedentTagId = goldenTag.Id,
            AntecedentTag = goldenTag,
            ConsequentTagId = dogTag.Id,
            ConsequentTag = dogTag,
            IsActive = true
        };
        await _context.BooruTagImplications.AddRangeAsync(implication1, implication2);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _sut.GetAllImpliedTagsAsync(new[] { tabbyTag, goldenTag })).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "cat");
        result.Should().Contain(t => t.Name == "dog");
    }

    #endregion

    #region Cycle Detection Tests

    [Fact]
    public async Task WouldCreateAliasCycleAsync_WithNoCycle_ReturnsFalse()
    {
        // Arrange: kitty -> cat (no cycle)
        var kittyTag = CreateTag("kitty");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(kittyTag, catTag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.WouldCreateAliasCycleAsync(kittyTag.Id, catTag.Id);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task WouldCreateAliasCycleAsync_WithDirectCycle_ReturnsTrue()
    {
        // Arrange: kitty -> cat exists, now checking if cat -> kitty would create cycle
        var kittyTag = CreateTag("kitty");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(kittyTag, catTag);

        var alias = new TagAlias
        {
            AliasTagId = kittyTag.Id,
            AliasTag = kittyTag,
            TargetTagId = catTag.Id,
            TargetTag = catTag,
            IsActive = true
        };
        await _context.BooruTagAliases.AddAsync(alias);
        await _context.SaveChangesAsync();

        // Act - Would cat -> kitty create a cycle?
        var result = await _sut.WouldCreateAliasCycleAsync(catTag.Id, kittyTag.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task WouldCreateImplicationCycleAsync_WithNoCycle_ReturnsFalse()
    {
        // Arrange: tabby_cat -> cat (no cycle)
        var tabbyTag = CreateTag("tabby_cat");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(tabbyTag, catTag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.WouldCreateImplicationCycleAsync(tabbyTag.Id, catTag.Id);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task WouldCreateImplicationCycleAsync_WithDirectCycle_ReturnsTrue()
    {
        // Arrange: tabby_cat -> cat exists, now checking if cat -> tabby_cat would create cycle
        var tabbyTag = CreateTag("tabby_cat");
        var catTag = CreateTag("cat");
        await _context.BooruTags.AddRangeAsync(tabbyTag, catTag);

        var implication = new TagImplication
        {
            AntecedentTagId = tabbyTag.Id,
            AntecedentTag = tabbyTag,
            ConsequentTagId = catTag.Id,
            ConsequentTag = catTag,
            IsActive = true
        };
        await _context.BooruTagImplications.AddAsync(implication);
        await _context.SaveChangesAsync();

        // Act - Would cat -> tabby_cat create a cycle?
        var result = await _sut.WouldCreateImplicationCycleAsync(catTag.Id, tabbyTag.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task WouldCreateImplicationCycleAsync_WithIndirectCycle_ReturnsTrue()
    {
        // Arrange: A -> B -> C exists, now checking if C -> A would create cycle
        var tagA = CreateTag("tag_a");
        var tagB = CreateTag("tag_b");
        var tagC = CreateTag("tag_c");
        await _context.BooruTags.AddRangeAsync(tagA, tagB, tagC);

        var implication1 = new TagImplication
        {
            AntecedentTagId = tagA.Id,
            AntecedentTag = tagA,
            ConsequentTagId = tagB.Id,
            ConsequentTag = tagB,
            IsActive = true
        };
        var implication2 = new TagImplication
        {
            AntecedentTagId = tagB.Id,
            AntecedentTag = tagB,
            ConsequentTagId = tagC.Id,
            ConsequentTag = tagC,
            IsActive = true
        };
        await _context.BooruTagImplications.AddRangeAsync(implication1, implication2);
        await _context.SaveChangesAsync();

        // Act - Would C -> A create a cycle?
        var result = await _sut.WouldCreateImplicationCycleAsync(tagC.Id, tagA.Id);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region CRUD Tests

    [Fact]
    public async Task CreateAliasAsync_WithValidData_CreatesAlias()
    {
        // Act
        var result = await _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "cat"
        }, null);

        // Assert
        result.Should().NotBeNull();
        result.AliasTag.Name.Should().Be("kitty");
        result.TargetTag.Name.Should().Be("cat");
        result.IsActive.Should().BeTrue();

        // Verify in database
        var aliasInDb = await _context.BooruTagAliases.FindAsync(result.Id);
        aliasInDb.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAliasAsync_WithSelfReference_ThrowsException()
    {
        // Arrange - Create a tag first
        var tag = CreateTag("cat");
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = () => _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "cat",
            TargetTagName = "cat"
        }, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot alias to itself*");
    }

    [Fact]
    public async Task CreateAliasAsync_WithExistingAlias_ThrowsException()
    {
        // Arrange - Create first alias
        await _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "cat"
        }, null);

        // Act & Assert - Try to create another alias for "kitty"
        var act = () => _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "feline"
        }, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already aliased*");
    }

    [Fact]
    public async Task CreateAliasAsync_WithCycle_ThrowsException()
    {
        // Arrange - Create first alias: kitty -> cat
        await _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "cat"
        }, null);

        // Act & Assert - Try to create cat -> kitty (cycle)
        var act = () => _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "cat",
            TargetTagName = "kitty"
        }, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Fact]
    public async Task DeleteAliasAsync_WithExistingAlias_DeletesAndReturnsTrue()
    {
        // Arrange
        var aliasDto = await _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "cat"
        }, null);

        // Act
        var result = await _sut.DeleteAliasAsync(aliasDto.Id);

        // Assert
        result.Should().BeTrue();
        var aliasInDb = await _context.BooruTagAliases.FindAsync(aliasDto.Id);
        aliasInDb.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAliasAsync_WithNonExistentAlias_ReturnsFalse()
    {
        // Act
        var result = await _sut.DeleteAliasAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateImplicationAsync_WithValidData_CreatesImplication()
    {
        // Act
        var result = await _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        }, null);

        // Assert
        result.Should().NotBeNull();
        result.AntecedentTag.Name.Should().Be("tabby_cat");
        result.ConsequentTag.Name.Should().Be("cat");
        result.IsActive.Should().BeTrue();

        // Verify in database
        var implicationInDb = await _context.BooruTagImplications.FindAsync(result.Id);
        implicationInDb.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateImplicationAsync_WithSelfReference_ThrowsException()
    {
        // Arrange
        var tag = CreateTag("cat");
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Act & Assert
        var act = () => _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "cat",
            ConsequentTagName = "cat"
        }, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot imply itself*");
    }

    [Fact]
    public async Task CreateImplicationAsync_WithExistingImplication_ThrowsException()
    {
        // Arrange - Create first implication
        await _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        }, null);

        // Act & Assert - Try to create same implication again
        var act = () => _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        }, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task CreateImplicationAsync_WithCycle_ThrowsException()
    {
        // Arrange - Create implication chain: A -> B -> C
        await _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "tag_a",
            ConsequentTagName = "tag_b"
        }, null);
        await _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "tag_b",
            ConsequentTagName = "tag_c"
        }, null);

        // Act & Assert - Try to create C -> A (cycle)
        var act = () => _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "tag_c",
            ConsequentTagName = "tag_a"
        }, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Fact]
    public async Task DeleteImplicationAsync_WithExistingImplication_DeletesAndReturnsTrue()
    {
        // Arrange
        var implicationDto = await _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        }, null);

        // Act
        var result = await _sut.DeleteImplicationAsync(implicationDto.Id);

        // Assert
        result.Should().BeTrue();
        var implicationInDb = await _context.BooruTagImplications.FindAsync(implicationDto.Id);
        implicationInDb.Should().BeNull();
    }

    [Fact]
    public async Task DeleteImplicationAsync_WithNonExistentImplication_ReturnsFalse()
    {
        // Act
        var result = await _sut.DeleteImplicationAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Pagination and Search Tests

    [Fact]
    public async Task GetAllAliasesAsync_ReturnsPagedResults()
    {
        // Arrange - Create multiple aliases
        for (int i = 1; i <= 15; i++)
        {
            await _sut.CreateAliasAsync(new CreateTagAliasRequest
            {
                AliasTagName = $"alias{i}",
                TargetTagName = $"target{i}"
            }, null);
        }

        // Act
        var page1 = await _sut.GetAllAliasesAsync(page: 1, pageSize: 10);
        var page2 = await _sut.GetAllAliasesAsync(page: 2, pageSize: 10);

        // Assert
        page1.TotalCount.Should().Be(15);
        page1.Items.Should().HaveCount(10);
        page2.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task SearchAliasesAsync_FindsMatchingAliases()
    {
        // Arrange
        await _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "cat"
        }, null);
        await _sut.CreateAliasAsync(new CreateTagAliasRequest
        {
            AliasTagName = "puppy",
            TargetTagName = "dog"
        }, null);

        // Act
        var result = (await _sut.SearchAliasesAsync("kit")).ToList();

        // Assert
        result.Should().HaveCount(1);
        result.First().AliasTag.Name.Should().Be("kitty");
    }

    [Fact]
    public async Task GetAllImplicationsAsync_ReturnsPagedResults()
    {
        // Arrange - Create multiple implications
        for (int i = 1; i <= 15; i++)
        {
            await _sut.CreateImplicationAsync(new CreateTagImplicationRequest
            {
                AntecedentTagName = $"specific{i}",
                ConsequentTagName = $"general{i}"
            }, null);
        }

        // Act
        var page1 = await _sut.GetAllImplicationsAsync(page: 1, pageSize: 10);
        var page2 = await _sut.GetAllImplicationsAsync(page: 2, pageSize: 10);

        // Assert
        page1.TotalCount.Should().Be(15);
        page1.Items.Should().HaveCount(10);
        page2.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task SearchImplicationsAsync_FindsMatchingImplications()
    {
        // Arrange
        await _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        }, null);
        await _sut.CreateImplicationAsync(new CreateTagImplicationRequest
        {
            AntecedentTagName = "golden_retriever",
            ConsequentTagName = "dog"
        }, null);

        // Act
        var result = (await _sut.SearchImplicationsAsync("cat")).ToList();

        // Assert
        result.Should().HaveCount(1);
        result.First().AntecedentTag.Name.Should().Be("tabby_cat");
    }

    #endregion

    #region Combined Resolution Tests

    [Fact]
    public async Task ResolveAndExpandTagsAsync_WithAliasesAndImplications_ResolvesAndExpands()
    {
        // Arrange:
        // - kitty -> cat (alias)
        // - tabby_cat -> cat (implication)
        // - cat -> mammal (implication)
        var kittyTag = CreateTag("kitty");
        var tabbyTag = CreateTag("tabby_cat");
        var catTag = CreateTag("cat");
        var mammalTag = CreateTag("mammal");
        await _context.BooruTags.AddRangeAsync(kittyTag, tabbyTag, catTag, mammalTag);

        var alias = new TagAlias
        {
            AliasTagId = kittyTag.Id,
            AliasTag = kittyTag,
            TargetTagId = catTag.Id,
            TargetTag = catTag,
            IsActive = true
        };
        await _context.BooruTagAliases.AddAsync(alias);

        var implication1 = new TagImplication
        {
            AntecedentTagId = tabbyTag.Id,
            AntecedentTag = tabbyTag,
            ConsequentTagId = catTag.Id,
            ConsequentTag = catTag,
            IsActive = true
        };
        var implication2 = new TagImplication
        {
            AntecedentTagId = catTag.Id,
            AntecedentTag = catTag,
            ConsequentTagId = mammalTag.Id,
            ConsequentTag = mammalTag,
            IsActive = true
        };
        await _context.BooruTagImplications.AddRangeAsync(implication1, implication2);
        await _context.SaveChangesAsync();

        // Act - Pass in kitty and tabby_cat
        var result = (await _sut.ResolveAndExpandTagsAsync(new[] { kittyTag, tabbyTag })).ToList();

        // Assert
        // kitty -> cat (resolved alias)
        // tabby_cat stays as tabby_cat
        // cat implies mammal
        // tabby_cat implies cat (which then implies mammal)
        result.Should().Contain(t => t.Name == "cat"); // kitty resolved to cat, tabby implies cat
        result.Should().Contain(t => t.Name == "tabby_cat"); // Original tag stays
        result.Should().Contain(t => t.Name == "mammal"); // cat implies mammal
        result.Should().NotContain(t => t.Name == "kitty"); // kitty was aliased to cat
    }

    #endregion

    #region Helper Methods

    private static Tag CreateTag(string name)
    {
        return new Tag
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = null,
            PostCount = 0
        };
    }

    #endregion
}
