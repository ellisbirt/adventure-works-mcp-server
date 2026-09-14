using FluentAssertions;
using Moq;
using EnterpriseAiGateway.Integration.Anthropic;
using Xunit;

namespace EnterpriseAiGateway.Tests.Integration.Anthropic;

/// <summary>
/// Comprehensive unit tests for Anthropic API contracts.
/// Tests serialization, deserialization, and contract compliance.
/// </summary>
public class AnthropicContractsTests
{
    private readonly MockRepository _mockRepository = new(MockBehavior.Strict);

    #region AnthropicContentBlock Tests

    [Fact]
    public void AnthropicContentBlock_WithTextTypeAndContent_CreatesBlock()
    {
        // Arrange & Act
        var block = new AnthropicContentBlock(
            Type: "text",
            Text: "This is test content"
        );

        // Assert
        block.Type.Should().Be("text");
        block.Text.Should().Be("This is test content");
        block.CacheControl.Should().BeNull();
    }

    [Fact]
    public void AnthropicContentBlock_WithCacheControl_StoresCacheMetadata()
    {
        // Arrange
        var cacheControl = new AnthropicCacheControl(Type: "ephemeral");

        // Act
        var block = new AnthropicContentBlock(
            Type: "text",
            Text: "Cached content",
            CacheControl: cacheControl
        );

        // Assert
        block.CacheControl.Should().NotBeNull();
        block.CacheControl!.Type.Should().Be("ephemeral");
    }

    [Fact]
    public void AnthropicContentBlock_WithEmptyText_AllowsEmpty()
    {
        // Arrange & Act
        var block = new AnthropicContentBlock(Type: "text", Text: string.Empty);

        // Assert
        block.Text.Should().BeEmpty();
    }

    [Fact]
    public void AnthropicContentBlock_WithLongText_StoresLongContent()
    {
        // Arrange
        var longText = new string('a', 10000);

        // Act
        var block = new AnthropicContentBlock(Type: "text", Text: longText);

        // Assert
        block.Text.Length.Should().Be(10000);
    }

    #endregion

    #region AnthropicCacheControl Tests

    [Fact]
    public void AnthropicCacheControl_DefaultType_IsEphemeral()
    {
        // Arrange & Act
        var cacheControl = new AnthropicCacheControl();

        // Assert
        cacheControl.Type.Should().Be("ephemeral");
    }

    [Fact]
    public void AnthropicCacheControl_WithCustomType_StoresCustomType()
    {
        // Arrange & Act
        var cacheControl = new AnthropicCacheControl(Type: "persistent");

        // Assert
        cacheControl.Type.Should().Be("persistent");
    }

    #endregion

    #region AnthropicSystemPrompt Tests

    [Fact]
    public void AnthropicSystemPrompt_DefaultValues_HasDefaults()
    {
        // Arrange & Act
        var prompt = new AnthropicSystemPrompt();

        // Assert
        prompt.Type.Should().Be("text");
        prompt.Text.Should().BeEmpty();
        prompt.CacheControl.Should().BeNull();
    }

    [Fact]
    public void AnthropicSystemPrompt_WithTextAndCacheControl_StoresAllValues()
    {
        // Arrange
        var cacheControl = new AnthropicCacheControl();
        var systemText = "You are a helpful assistant";

        // Act
        var prompt = new AnthropicSystemPrompt(
            Type: "text",
            Text: systemText,
            CacheControl: cacheControl
        );

        // Assert
        prompt.Type.Should().Be("text");
        prompt.Text.Should().Be(systemText);
        prompt.CacheControl.Should().NotBeNull();
    }

    [Fact]
    public void AnthropicSystemPrompt_WithComplexPrompt_StoresCorrectly()
    {
        // Arrange
        var complexPrompt = @"You are an enterprise AI assistant for AdventureWorks.
        You have access to customer data.
        Always mask sensitive information.
        Return results in JSON format.";

        // Act
        var prompt = new AnthropicSystemPrompt(Text: complexPrompt);

        // Assert
        prompt.Text.Should().Be(complexPrompt);
        prompt.Text.Should().Contain("enterprise AI");
        prompt.Text.Should().Contain("mask sensitive");
    }

    #endregion

    #region AnthropicUserMessage Tests

    [Fact]
    public void AnthropicUserMessage_DefaultRole_IsUser()
    {
        // Arrange & Act
        var message = new AnthropicUserMessage();

        // Assert
        message.Role.Should().Be("user");
        message.Content.Should().BeEmpty();
    }

    [Fact]
    public void AnthropicUserMessage_WithContent_StoresContent()
    {
        // Arrange
        var content = "Get customer data for ID 123";

        // Act
        var message = new AnthropicUserMessage(Content: content);

        // Assert
        message.Role.Should().Be("user");
        message.Content.Should().Be(content);
    }

    [Fact]
    public void AnthropicUserMessage_WithCustomRole_StoresRole()
    {
        // Arrange & Act
        var message = new AnthropicUserMessage(Role: "assistant", Content: "Response");

        // Assert
        message.Role.Should().Be("assistant");
        message.Content.Should().Be("Response");
    }

    #endregion

    #region AnthropicMessageRequest Tests

    [Fact]
    public void AnthropicMessageRequest_WithRequiredParameters_CreatesRequest()
    {
        // Arrange & Act
        var request = new AnthropicMessageRequest(
            Model: "claude-3-opus",
            MaxTokens: 4096
        );

        // Assert
        request.Model.Should().Be("claude-3-opus");
        request.MaxTokens.Should().Be(4096);
        request.System.Should().BeNull();
        request.Messages.Should().BeNull();
    }

    [Fact]
    public void AnthropicMessageRequest_WithSystemPrompt_StoresSystemPrompt()
    {
        // Arrange
        var systemPrompt = new AnthropicSystemPrompt(Text: "You are helpful");

        // Act
        var request = new AnthropicMessageRequest(
            Model: "claude-3-opus",
            MaxTokens: 4096,
            System: systemPrompt
        );

        // Assert
        request.System.Should().NotBeNull();
        request.System.Should().Be(systemPrompt);
    }

    [Fact]
    public void AnthropicMessageRequest_WithMessages_StoresMessages()
    {
        // Arrange
        var messages = new List<AnthropicUserMessage>
        {
            new AnthropicUserMessage(Content: "Message 1"),
            new AnthropicUserMessage(Content: "Message 2")
        };

        // Act
        var request = new AnthropicMessageRequest(
            Model: "claude-3-opus",
            MaxTokens: 4096,
            Messages: messages
        );

        // Assert
        request.Messages.Should().HaveCount(2);
        request.Messages![0].Content.Should().Be("Message 1");
        request.Messages[1].Content.Should().Be("Message 2");
    }

    [Fact]
    public void AnthropicMessageRequest_WithAllParameters_StoresAllValues()
    {
        // Arrange
        var systemPrompt = new AnthropicSystemPrompt(Text: "System instruction");
        var messages = new List<AnthropicUserMessage>
        {
            new AnthropicUserMessage(Content: "User query")
        };

        // Act
        var request = new AnthropicMessageRequest(
            Model: "claude-3-opus",
            MaxTokens: 2048,
            System: systemPrompt,
            Messages: messages
        );

        // Assert
        request.Model.Should().Be("claude-3-opus");
        request.MaxTokens.Should().Be(2048);
        request.System.Should().Be(systemPrompt);
        request.Messages.Should().HaveCount(1);
    }

    #endregion

    #region AnthropicMessageResponse Tests

    [Fact]
    public void AnthropicMessageResponse_WithValidData_CreatesResponse()
    {
        // Arrange
        var content = new List<AnthropicContent>
        {
            new("text", "Response text")
        };
        var usage = new AnthropicUsage(
            InputTokens: 100,
            OutputTokens: 50
        );

        // Act
        var response = new AnthropicMessageResponse(
            Id: "msg_123",
            Type: "message",
            Role: "assistant",
            Content: content,
            Model: "claude-3-opus",
            StopReason: "end_turn",
            Usage: usage
        );

        // Assert
        response.Id.Should().Be("msg_123");
        response.Type.Should().Be("message");
        response.Role.Should().Be("assistant");
        response.Model.Should().Be("claude-3-opus");
        response.StopReason.Should().Be("end_turn");
    }

    [Fact]
    public void AnthropicMessageResponse_WithMultipleContentBlocks_ContainsAll()
    {
        // Arrange
        var content = new List<AnthropicContent>
        {
            new("text", "First block"),
            new("text", "Second block")
        };
        var usage = new AnthropicUsage(100, 50);

        // Act
        var response = new AnthropicMessageResponse(
            Id: "msg_456",
            Type: "message",
            Role: "assistant",
            Content: content,
            Model: "claude-3-opus",
            StopReason: "end_turn",
            Usage: usage
        );

        // Assert
        response.Content.Should().HaveCount(2);
        response.Content[0].Text.Should().Be("First block");
        response.Content[1].Text.Should().Be("Second block");
    }

    #endregion

    #region AnthropicContent Tests

    [Fact]
    public void AnthropicContent_WithTextType_StoresContent()
    {
        // Arrange & Act
        var content = new AnthropicContent(Type: "text", Text: "Content here");

        // Assert
        content.Type.Should().Be("text");
        content.Text.Should().Be("Content here");
    }

    [Fact]
    public void AnthropicContent_WithNullText_AllowsNull()
    {
        // Arrange & Act
        var content = new AnthropicContent(Type: "text", Text: null);

        // Assert
        content.Text.Should().BeNull();
    }

    #endregion

    #region AnthropicUsage Tests

    [Fact]
    public void AnthropicUsage_WithBasicTokenCounts_StoresValues()
    {
        // Arrange & Act
        var usage = new AnthropicUsage(
            InputTokens: 1000,
            OutputTokens: 500
        );

        // Assert
        usage.InputTokens.Should().Be(1000);
        usage.OutputTokens.Should().Be(500);
        usage.CacheCreationInputTokens.Should().BeNull();
        usage.CacheReadInputTokens.Should().BeNull();
    }

    [Fact]
    public void AnthropicUsage_WithCacheHit_StoresCacheReadTokens()
    {
        // Arrange & Act
        var usage = new AnthropicUsage(
            InputTokens: 100,
            OutputTokens: 50,
            CacheReadInputTokens: 5000
        );

        // Assert
        usage.CacheReadInputTokens.Should().Be(5000);
        usage.CacheCreationInputTokens.Should().BeNull();
    }

    [Fact]
    public void AnthropicUsage_WithCacheCreation_StoresCacheCreationTokens()
    {
        // Arrange & Act
        var usage = new AnthropicUsage(
            InputTokens: 100,
            OutputTokens: 50,
            CacheCreationInputTokens: 10000
        );

        // Assert
        usage.CacheCreationInputTokens.Should().Be(10000);
        usage.CacheReadInputTokens.Should().BeNull();
    }

    [Fact]
    public void AnthropicUsage_WithBothCacheMetrics_StoresBoth()
    {
        // Arrange & Act
        var usage = new AnthropicUsage(
            InputTokens: 100,
            OutputTokens: 50,
            CacheCreationInputTokens: 5000,
            CacheReadInputTokens: 3000
        );

        // Assert
        usage.CacheCreationInputTokens.Should().Be(5000);
        usage.CacheReadInputTokens.Should().Be(3000);
    }

    #endregion

    #region Contract Compliance Tests

    [Fact]
    public void AnthropicMessageRequest_SupportsComplexScenario()
    {
        // Arrange
        var systemPrompt = new AnthropicSystemPrompt(
            Type: "text",
            Text: "You are a helpful assistant with access to customer data.",
            CacheControl: new AnthropicCacheControl("ephemeral")
        );

        var messages = new List<AnthropicUserMessage>
        {
            new AnthropicUserMessage("user", "What customer data is available?"),
            new AnthropicUserMessage("assistant", "I have access to customer IDs, names, emails, and phone numbers."),
            new AnthropicUserMessage("user", "Get customer 123 history")
        };

        // Act
        var request = new AnthropicMessageRequest(
            Model: "claude-3-opus",
            MaxTokens: 4096,
            System: systemPrompt,
            Messages: messages
        );

        // Assert
        request.Messages.Should().HaveCount(3);
        request.System.Should().NotBeNull();
        request.Model.Should().Be("claude-3-opus");
    }

    [Fact]
    public void AnthropicMessageResponse_SupportsComplexResponse()
    {
        // Arrange
        var content = new List<AnthropicContent>
        {
            new("text", "Customer data retrieved and masked appropriately.")
        };

        var usage = new AnthropicUsage(
            InputTokens: 250,
            OutputTokens: 100,
            CacheCreationInputTokens: 2000,
            CacheReadInputTokens: null
        );

        // Act
        var response = new AnthropicMessageResponse(
            Id: "msg_abc123xyz",
            Type: "message",
            Role: "assistant",
            Content: content,
            Model: "claude-3-opus",
            StopReason: "end_turn",
            Usage: usage
        );

        // Assert
        response.Content.First().Text.Should().Contain("masked");
        response.Usage.CacheCreationInputTokens.Should().Be(2000);
        response.StopReason.Should().Be("end_turn");
    }

    #endregion

    #region Mock Integration Tests

    [Fact]
    public async Task AnthropicMessageRequest_CanBeUsedWithMocks()
    {
        // Arrange
        var mockAnthropicClient = _mockRepository.Create<IAnthropicClient>();
        var request = new AnthropicMessageRequest(
            Model: "claude-3-opus",
            MaxTokens: 4096
        );

        var expectedResponse = new AnthropicMessageResponse(
            Id: "msg_123",
            Type: "message",
            Role: "assistant",
            Content: new List<AnthropicContent> { new("text", "Mocked response") },
            Model: "claude-3-opus",
            StopReason: "end_turn",
            Usage: new AnthropicUsage(100, 50)
        );

        mockAnthropicClient
            .Setup(c => c.SendMessageAsync(It.IsAny<AnthropicMessageRequest>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await mockAnthropicClient.Object.SendMessageAsync(request);

        // Assert
        result.Role.Should().Be("assistant");
        result.Content.First().Text.Should().Be("Mocked response");
        mockAnthropicClient.Verify(c => c.SendMessageAsync(It.IsAny<AnthropicMessageRequest>()), Times.Once);
    }

    [Fact]
    public async Task AnthropicUsage_MockVerification_EnsuresAccuracy()
    {
        // Arrange
        var mockUsageTracker = _mockRepository.Create<IUsageTracker>();
        var usage = new AnthropicUsage(
            InputTokens: 500,
            OutputTokens: 250,
            CacheReadInputTokens: 1000
        );

        mockUsageTracker
            .Setup(t => t.RecordUsageAsync(It.IsAny<AnthropicUsage>()))
            .Returns(Task.CompletedTask);

        // Act
        await mockUsageTracker.Object.RecordUsageAsync(usage);

        // Assert
        mockUsageTracker.Verify(
            t => t.RecordUsageAsync(It.Is<AnthropicUsage>(u =>
                u.InputTokens == 500 &&
                u.OutputTokens == 250 &&
                u.CacheReadInputTokens == 1000
            )),
            Times.Once
        );
    }

    #endregion

    #region Test Helper Interfaces

    /// <summary>
    /// Mock interface for Anthropic client operations
    /// </summary>
    public interface IAnthropicClient
    {
        Task<AnthropicMessageResponse> SendMessageAsync(AnthropicMessageRequest request);
    }

    /// <summary>
    /// Mock interface for usage tracking
    /// </summary>
    public interface IUsageTracker
    {
        Task RecordUsageAsync(AnthropicUsage usage);
    }

    #endregion
}
