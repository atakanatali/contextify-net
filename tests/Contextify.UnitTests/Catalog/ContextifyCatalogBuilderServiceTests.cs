using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contextify.Config.Abstractions.Policy;
using Contextify.Core.Catalog;
using FluentAssertions;
using Xunit;

namespace Contextify.UnitTests.Catalog;

public sealed class ContextifyCatalogBuilderServiceTests
{
    private readonly ContextifyCatalogBuilderService _builder;

    public ContextifyCatalogBuilderServiceTests()
    {
        _builder = new ContextifyCatalogBuilderService();
    }

    [Fact]
    public async Task BuildSnapshotFromPolicyAsync_WithValidPolicy_ReturnsCorrectSnapshot()
    {
        // Arrange
        var policyConfig = new ContextifyPolicyConfigDto
        {
            SourceVersion = "v1",
            Whitelist = new List<ContextifyEndpointPolicyDto>
            {
                new()
                {
                    ToolName = "tool1",
                    OperationId = "op1",
                    RouteTemplate = "/test1",
                    HttpMethod = "GET",
                    Enabled = true
                },
                new()
                {
                    ToolName = "tool2",
                    OperationId = "op2",
                    RouteTemplate = "/test2",
                    HttpMethod = "POST",
                    Enabled = true
                }
            }
        };

        // Act
        var snapshot = await _builder.BuildSnapshotFromPolicyAsync(policyConfig);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.ToolCount.Should().Be(2);
        snapshot.PolicySourceVersion.Should().Be("v1");
        
        snapshot.ToolsByName.Should().ContainKey("tool1");
        snapshot.ToolsByName.Should().ContainKey("tool2");

        var tool1 = snapshot.ToolsByName["tool1"];
        tool1.ToolName.Should().Be("tool1");
        tool1.EndpointDescriptor.Should().NotBeNull();
        tool1.EndpointDescriptor!.OperationId.Should().Be("op1");
        tool1.EndpointDescriptor.RouteTemplate.Should().Be("/test1");
    }

    [Fact]
    public async Task BuildSnapshotFromPolicyAsync_WithDisabledTool_SkipsDisabledTool()
    {
        // Arrange
        var policyConfig = new ContextifyPolicyConfigDto
        {
            Whitelist = new List<ContextifyEndpointPolicyDto>
            {
                new()
                {
                    ToolName = "enabled_tool",
                    Enabled = true
                },
                new()
                {
                    ToolName = "disabled_tool",
                    Enabled = false
                }
            }
        };

        // Act
        var snapshot = await _builder.BuildSnapshotFromPolicyAsync(policyConfig);

        // Assert
        snapshot.ToolCount.Should().Be(1);
        snapshot.ToolsByName.Should().ContainKey("enabled_tool");
        snapshot.ToolsByName.Should().NotContainKey("disabled_tool");
    }

    [Fact]
    public async Task BuildSnapshotFromPolicyAsync_WithNamelessTool_SkipsNamelessTool()
    {
        // Arrange
        var policyConfig = new ContextifyPolicyConfigDto
        {
            Whitelist = new List<ContextifyEndpointPolicyDto>
            {
                new()
                {
                    ToolName = null, // Nameless
                    Enabled = true
                },
                new()
                {
                    ToolName = "valid_tool",
                    Enabled = true
                }
            }
        };

        // Act
        var snapshot = await _builder.BuildSnapshotFromPolicyAsync(policyConfig);

        // Assert
        snapshot.ToolCount.Should().Be(1);
        snapshot.ToolsByName.Should().ContainKey("valid_tool");
    }

    [Fact]
    public async Task BuildSnapshotFromPolicyAsync_WithDuplicateToolNames_PreservesFirstOccurrence()
    {
        // Arrange
        var policyConfig = new ContextifyPolicyConfigDto
        {
            Whitelist = new List<ContextifyEndpointPolicyDto>
            {
                new()
                {
                    ToolName = "duplicate",
                    OperationId = "first",
                    Enabled = true
                },
                new()
                {
                    ToolName = "duplicate",
                    OperationId = "second",
                    Enabled = true
                }
            }
        };

        // Act
        var snapshot = await _builder.BuildSnapshotFromPolicyAsync(policyConfig);

        // Assert
        snapshot.ToolCount.Should().Be(1);
        snapshot.ToolsByName.Should().ContainKey("duplicate");
        snapshot.ToolsByName["duplicate"].EndpointDescriptor!.OperationId.Should().Be("first");
    }

    [Fact]
    public void BuildSnapshotFromPolicy_Sync_ReturnsCorrectSnapshot()
    {
        // Arrange
        var policyConfig = new ContextifyPolicyConfigDto
        {
            Whitelist = new List<ContextifyEndpointPolicyDto>
            {
                new()
                {
                    ToolName = "sync_tool",
                    Enabled = true
                }
            }
        };

        // Act
        var snapshot = _builder.BuildSnapshotFromPolicy(policyConfig);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.ToolCount.Should().Be(1);
        snapshot.ToolsByName.Should().ContainKey("sync_tool");
    }

    [Fact]
    public async Task BuildSnapshotFromPolicyAsync_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _builder.BuildSnapshotFromPolicyAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
