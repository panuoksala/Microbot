# YouTrack Skill Implementation Plan

## Overview

The YouTrack skill provides integration with JetBrains YouTrack issue tracker, allowing Microbot to interact with issues, projects, and comments. It supports two permission modes and uses permanent token authentication.

## Permission Modes

### ReadOnly Mode
- List and search issues
- Get issue details
- List comments on issues
- List and get project information
- Get current user information

### FullControl Mode
All ReadOnly capabilities plus:
- Create new issues
- Update existing issues (summary, description)
- Apply commands to issues (change state, assignee, priority, etc.)
- Add comments to issues
- Update existing comments

## Authentication

The skill uses YouTrack's permanent token authentication:

1. User generates a permanent token in YouTrack:
   - Profile → Account Security → Tokens → New Token
2. Token is stored in `Microbot.config` under `Skills.YouTrack.PermanentToken`
3. Token is sent as Bearer token in Authorization header

## Configuration

```json
{
  "Skills": {
    "YouTrack": {
      "Enabled": true,
      "Mode": "ReadOnly",  // or "FullControl"
      "BaseUrl": "https://youtrack.example.com",
      "PermanentToken": "perm:your-token-here"
    }
  }
}
```

## Project Structure

```
src/Microbot.Skills.YouTrack/
├── Microbot.Skills.YouTrack.csproj
├── YouTrackSkill.cs              # Main skill with Semantic Kernel functions
├── YouTrackSkillMode.cs          # Permission mode enum
├── Models/
│   ├── YouTrackProject.cs        # Project model
│   ├── YouTrackIssue.cs          # Issue model
│   ├── YouTrackComment.cs        # Comment model
│   └── YouTrackUser.cs           # User model
└── Services/
    └── YouTrackApiClient.cs      # HTTP client for YouTrack REST API
```

## Semantic Kernel Functions

### Project Functions
| Function | Description | Mode |
|----------|-------------|------|
| `list_projects` | Lists all accessible projects | ReadOnly |
| `get_project` | Gets details of a specific project | ReadOnly |

### Issue Functions
| Function | Description | Mode |
|----------|-------------|------|
| `get_issue` | Gets details of a specific issue | ReadOnly |
| `search_issues` | Searches issues using YouTrack query syntax | ReadOnly |
| `list_project_issues` | Lists issues in a specific project | ReadOnly |
| `create_issue` | Creates a new issue | FullControl |
| `update_issue` | Updates issue summary/description | FullControl |
| `apply_command` | Applies a command to an issue | FullControl |

### Comment Functions
| Function | Description | Mode |
|----------|-------------|------|
| `list_comments` | Lists comments on an issue | ReadOnly |
| `add_comment` | Adds a comment to an issue | FullControl |
| `update_comment` | Updates an existing comment | FullControl |

### User Functions
| Function | Description | Mode |
|----------|-------------|------|
| `get_current_user` | Gets current authenticated user info | ReadOnly |

## YouTrack Query Syntax Examples

The `search_issues` function supports YouTrack's powerful query syntax:

```
# By project
project: PROJ

# By state
state: Open
state: {In Progress}

# By assignee
assignee: john
assignee: me

# By priority
priority: Critical

# By type
type: Bug

# By tag
#bug
#feature

# By date
created: today
created: {last week}
updated: {this month}

# Combined queries
project: PROJ state: Open assignee: me
project: PROJ #bug priority: Critical
```

## Command Examples

The `apply_command` function supports YouTrack commands:

```
# Change state
State In Progress
State Done

# Change assignee
Assignee john
Assignee Unassigned

# Change priority
Priority Critical
Priority Normal

# Change type
Type Bug
Type Feature

# Add tag
tag important

# Multiple changes
State Done Assignee john
```

## API Endpoints Used

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/users/me` | GET | Get current user |
| `/api/admin/projects` | GET | List projects |
| `/api/admin/projects/{id}` | GET | Get project details |
| `/api/issues` | GET | Search issues |
| `/api/issues/{id}` | GET | Get issue details |
| `/api/issues` | POST | Create issue |
| `/api/issues/{id}` | POST | Update issue |
| `/api/issues/{id}/commands` | POST | Apply command |
| `/api/issues/{id}/comments` | GET | List comments |
| `/api/issues/{id}/comments` | POST | Add comment |
| `/api/issues/{id}/comments/{commentId}` | POST | Update comment |

## Error Handling

- Invalid token: Returns authentication error
- Issue not found: Returns "Issue not found: {id}"
- Project not found: Returns "Project not found: {id}"
- Permission denied: Returns mode-specific error message
- API errors: Logged and re-thrown with context

## Implementation Status

- [x] Project structure created
- [x] YouTrackSkillMode enum
- [x] YouTrack models (Project, Issue, Comment, User)
- [x] YouTrackApiClient with HTTP client
- [x] YouTrackSkill with Semantic Kernel functions
- [x] YouTrackSkillLoader
- [x] Configuration model (YouTrackSkillConfig)
- [x] SkillManager integration
- [x] Solution file updated
- [x] AGENTS.md updated
- [x] Implementation plan documentation

## Future Enhancements

1. **Attachment support** - Upload/download attachments
2. **Work items** - Track time spent on issues
3. **Agile boards** - Interact with agile boards and sprints
4. **Custom fields** - Better support for custom field types
5. **Webhooks** - Real-time notifications (would require separate service)
6. **Issue links** - Create/manage issue relationships
7. **Saved searches** - Use and manage saved searches
