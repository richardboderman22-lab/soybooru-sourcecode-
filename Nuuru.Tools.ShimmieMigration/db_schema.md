# Shimmie2 Database Schema Documentation

This document provides comprehensive documentation of the Shimmie2 database schema, including all tables, columns, relationships, and indexes.

## Table of Contents

1. [Overview](#overview)
2. [Type Definitions](#type-definitions)
3. [Core Tables](#core-tables)
4. [Extension Tables](#extension-tables)
5. [Relationships](#relationships)
6. [Database Engine Support](#database-engine-support)

---

## Overview

Shimmie2 uses a modular database schema where core tables handle fundamental functionality (users, images, tags) and extension tables add optional features (comments, pools, forums, etc.). The schema is created and maintained through extension upgrade events, allowing for dynamic schema management.

---

## Type Definitions

The following custom types are used throughout the schema:

| Type | MySQL | PostgreSQL | SQLite |
|------|-------|------------|--------|
| **SCORE_AIPK** | `INTEGER PRIMARY KEY AUTO_INCREMENT` | `INTEGER NOT NULL PRIMARY KEY GENERATED ALWAYS AS IDENTITY` | `INTEGER PRIMARY KEY AUTOINCREMENT` |
| **SCORE_INET** | `VARCHAR(45)` | `INET` | `VARCHAR(45)` |

---

## Core Tables

These tables are created during installation and form the foundation of the system.

### aliases

Stores tag aliases that map one tag name to another.

| Column | Type | Constraints |
|--------|------|-------------|
| `oldtag` | VARCHAR(128) | PRIMARY KEY, NOT NULL |
| `newtag` | VARCHAR(128) | NOT NULL |

**Indexes:**
- `newtag_idx` on `newtag`

---

### config

Stores system configuration key-value pairs.

| Column | Type | Constraints |
|--------|------|-------------|
| `name` | VARCHAR(128) | PRIMARY KEY, NOT NULL |
| `value` | TEXT | |

---

### users

Stores user accounts.

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `name` | VARCHAR(32) | UNIQUE, NOT NULL |
| `pass` | VARCHAR(250) | |
| `joindate` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `class` | VARCHAR(32) | NOT NULL, DEFAULT 'user' |
| `email` | VARCHAR(128) | |

**Indexes:**
- `name_idx` on `name`

---

### images

Stores uploaded image/media posts. This is the central content table.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY | Unique post ID |
| `owner_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE RESTRICT | User who uploaded |
| `owner_ip` | SCORE_INET | NOT NULL | IP address of uploader |
| `filename` | VARCHAR(64) | NOT NULL | Original filename |
| `filesize` | INTEGER | NOT NULL | File size in bytes |
| `hash` | CHAR(32) | UNIQUE, NOT NULL | MD5 hash of file |
| `ext` | CHAR(4) | NOT NULL | File extension |
| `source` | VARCHAR(255) | | Source URL |
| `width` | INTEGER | NOT NULL | Image width in pixels |
| `height` | INTEGER | NOT NULL | Image height in pixels |
| `posted` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP | Upload timestamp |
| `locked` | BOOLEAN | NOT NULL, DEFAULT FALSE | Whether post is locked |
| `lossless` | BOOLEAN | NULL | Whether media is lossless |
| `video` | BOOLEAN | NULL | Whether file is video |
| `audio` | BOOLEAN | NULL | Whether file has audio |
| `length` | INTEGER | NULL | Media duration in seconds |
| `mime` | VARCHAR(512) | NULL | MIME type |

**Extension-Added Columns:**

| Column | Type | Default | Added By |
|--------|------|---------|----------|
| `approved` | BOOLEAN | FALSE | approval |
| `approved_by_id` | INTEGER | NULL | approval |
| `author` | VARCHAR(255) | NULL | artists |
| `comments_locked` | BOOLEAN | FALSE | comment |
| `favorites` | INTEGER | 0 | favorites |
| `image` | BOOLEAN | NULL | media |
| `video_codec` | VARCHAR(512) | NULL | media |
| `notes` | INTEGER | 0 | notes |
| `numeric_score` | INTEGER | 0 | numeric_score |
| `title` | VARCHAR(255) | NULL | post_titles |
| `private` | BOOLEAN | FALSE | private_image |
| `rating` | CHAR(1) | 'u' | rating |
| `trash` | BOOLEAN | FALSE | trash |

**Indexes:**
- `owner_id_idx` on `owner_id`
- `width_idx` on `width`
- `height_idx` on `height`
- `hash_idx` on `hash`
- `approved_idx` on `approved`
- `comments_locked_idx` on `comments_locked`
- `private_idx` on `private`
- `rating_idx` on `rating`

---

### tags

Stores unique tag definitions with usage counts.

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `tag` | VARCHAR(64) | UNIQUE, NOT NULL |
| `count` | INTEGER | NOT NULL, DEFAULT 0 |

**Indexes:**
- `tag_idx` on `tag`

---

### image_tags

Junction table linking images to tags (many-to-many).

| Column | Type | Constraints |
|--------|------|-------------|
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `tag_id` | INTEGER | NOT NULL, FK → tags(id) ON DELETE CASCADE |

**Indexes:**
- `images_tags_image_id_idx` on `image_id`
- `images_tags_tag_id_idx` on `tag_id`

**Constraints:**
- UNIQUE (`image_id`, `tag_id`)

---

## Extension Tables

These tables are created by extensions and provide optional functionality.

### Artists Extension

#### artists

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `name` | VARCHAR(255) | NOT NULL |
| `created` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `updated` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `notes` | TEXT | |

#### artist_members

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `artist_id` | INTEGER | NOT NULL, FK → artists(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `name` | VARCHAR(255) | NOT NULL |
| `created` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `updated` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |

#### artist_alias

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `artist_id` | INTEGER | NOT NULL, FK → artists(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `alias` | VARCHAR(255) | |
| `created` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `updated` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |

#### artist_urls

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `artist_id` | INTEGER | NOT NULL, FK → artists(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `url` | VARCHAR(1000) | NOT NULL |
| `created` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `updated` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |

---

### Auto Tagger Extension

#### auto_tag

| Column | Type | Constraints |
|--------|------|-------------|
| `tag` | VARCHAR(128) | PRIMARY KEY, NOT NULL |
| `additional_tags` | VARCHAR(2000) | NOT NULL |

**Indexes:**
- `auto_tag_lower_tag_idx` on `LOWER(tag)` (PostgreSQL only)

---

### Blocks Extension

#### blocks

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `pages` | VARCHAR(128) | NOT NULL |
| `title` | VARCHAR(128) | NOT NULL |
| `area` | VARCHAR(16) | NOT NULL |
| `priority` | INTEGER | NOT NULL |
| `content` | TEXT | NOT NULL |
| `userclass` | TEXT | |

**Indexes:**
- `blocks_pages_idx` on `pages`

---

### Blotter Extension

#### blotter

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `entry_date` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `entry_text` | TEXT | NOT NULL |
| `important` | BOOLEAN | NOT NULL, DEFAULT FALSE |

---

### Comment Extension

#### comments

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `owner_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE RESTRICT |
| `owner_ip` | SCORE_INET | NOT NULL |
| `posted` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `comment` | TEXT | NOT NULL |

**Indexes:**
- `comments_image_id_idx` on `image_id`
- `comments_owner_id_idx` on `owner_id`
- `comments_posted_idx` on `posted`

---

### ET Server Extension

#### registration

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `responded` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `data` | TEXT | NOT NULL |

---

### Favorites Extension

#### user_favorites

| Column | Type | Constraints |
|--------|------|-------------|
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE CASCADE |
| `created_at` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |

---

### Forum Extension

#### forum_threads

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE RESTRICT |
| `title` | VARCHAR(255) | NOT NULL |
| `date` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `uptodate` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `response_count` | INTEGER | NOT NULL, DEFAULT 0 |
| `sticky` | BOOLEAN | NOT NULL, DEFAULT FALSE |

**Indexes:**
- `forum_threads_date_idx` on `date`

#### forum_posts

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `thread_id` | INTEGER | NOT NULL, FK → forum_threads(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE RESTRICT |
| `date` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `message` | TEXT | |

**Indexes:**
- `forum_posts_date_idx` on `date`

---

### Image Hash Ban Extension

#### image_bans

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `hash` | CHAR(32) | NOT NULL |
| `date` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `reason` | TEXT | NOT NULL |

---

### Image View Counter Extension

#### image_views

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `image_id` | INTEGER | NOT NULL |
| `user_id` | INTEGER | NOT NULL |
| `timestamp` | INTEGER | NOT NULL |
| `ipaddress` | SCORE_INET | NOT NULL |

---

### IP Ban Extension

#### bans

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `ip` | SCORE_INET | NOT NULL |
| `mode` | VARCHAR(64) | |
| `reason` | TEXT | |
| `banner_id` | INTEGER | NOT NULL, FK → users(id) |
| `added` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `expires` | TIMESTAMP | |

---

### Log DB Extension

#### score_log

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `username` | VARCHAR(32) | |
| `action` | VARCHAR(32) | NOT NULL |
| `priority` | INTEGER | NOT NULL |
| `address` | SCORE_INET | NOT NULL |
| `message` | TEXT | NOT NULL |
| `timestamp` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |

---

### Notes Extension

#### notes

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `enable` | INTEGER | NOT NULL |
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `user_ip` | SCORE_INET | NOT NULL |
| `date` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `x1` | INTEGER | NOT NULL |
| `y1` | INTEGER | NOT NULL |
| `height` | INTEGER | NOT NULL |
| `width` | INTEGER | NOT NULL |
| `note` | TEXT | NOT NULL |

**Indexes:**
- `notes_image_id_idx` on `image_id`

#### note_request

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `date` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |

**Indexes:**
- `note_request_image_id_idx` on `image_id`

#### note_histories

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `note_enable` | INTEGER | NOT NULL |
| `note_id` | INTEGER | NOT NULL, FK → notes(id) ON DELETE CASCADE |
| `review_id` | INTEGER | NOT NULL |
| `image_id` | INTEGER | NOT NULL |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `user_ip` | SCORE_INET | NOT NULL |
| `date` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `x1` | INTEGER | NOT NULL |
| `y1` | INTEGER | NOT NULL |
| `height` | INTEGER | NOT NULL |
| `width` | INTEGER | NOT NULL |
| `note` | TEXT | NOT NULL |

**Indexes:**
- `note_histories_image_id_idx` on `image_id`

---

### Not A Tag Extension

#### untags

| Column | Type | Constraints |
|--------|------|-------------|
| `tag` | VARCHAR(128) | PRIMARY KEY, NOT NULL |
| `redirect` | VARCHAR(255) | NOT NULL |

---

### Numeric Score Extension

#### numeric_score_votes

| Column | Type | Constraints |
|--------|------|-------------|
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE CASCADE |
| `score` | INTEGER | NOT NULL |

**Indexes:**
- `numeric_score_votes_image_id_idx` on `image_id`
- `numeric_score_votes__user_votes` on (`user_id`, `score`)

**Constraints:**
- UNIQUE (`image_id`, `user_id`)

---

### Permission Manager Extension

#### user_classes

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `name` | VARCHAR(32) | UNIQUE, NOT NULL |
| `parent` | VARCHAR(32) | NOT NULL |
| `description` | TEXT | NOT NULL |

#### user_class_permissions

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `user_class_id` | INTEGER | NOT NULL, FK → user_classes(id) ON DELETE CASCADE |
| `permission` | VARCHAR(32) | NOT NULL |
| `value` | BOOLEAN | NOT NULL |

**Indexes:**
- `user_class_permissions__user_class_id` on `user_class_id`

**Constraints:**
- UNIQUE (`user_class_id`, `permission`)

---

### PM (Private Message) Extension

#### private_message

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `from_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE CASCADE |
| `from_ip` | SCORE_INET | NOT NULL |
| `to_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE CASCADE |
| `sent_date` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `subject` | VARCHAR(64) | NOT NULL |
| `message` | TEXT | NOT NULL |
| `is_read` | BOOLEAN | NOT NULL, DEFAULT FALSE |

**Indexes:**
- `private_message__to_id` on `to_id`

---

### Pools Extension

#### pools

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `public` | BOOLEAN | NOT NULL, DEFAULT FALSE |
| `title` | VARCHAR(255) | UNIQUE, NOT NULL |
| `description` | TEXT | |
| `date` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `lastupdated` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `posts` | INTEGER | NOT NULL, DEFAULT 0 |

#### pool_images

| Column | Type | Constraints |
|--------|------|-------------|
| `pool_id` | INTEGER | NOT NULL, FK → pools(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `image_order` | INTEGER | NOT NULL, DEFAULT 0 |

#### pool_history

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `pool_id` | INTEGER | NOT NULL, FK → pools(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON UPDATE CASCADE ON DELETE CASCADE |
| `action` | INTEGER | NOT NULL |
| `images` | TEXT | |
| `count` | INTEGER | NOT NULL, DEFAULT 0 |
| `date` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |

---

### Post Description Extension

#### image_descriptions

| Column | Type | Constraints |
|--------|------|-------------|
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `description` | TEXT | |

**Constraints:**
- UNIQUE (`image_id`)

---

### Report Image Extension

#### image_reports

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `reporter_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE CASCADE |
| `reason` | TEXT | NOT NULL |

---

### S3 Extension

#### s3_sync_queue

| Column | Type | Constraints |
|--------|------|-------------|
| `hash` | CHAR(32) | PRIMARY KEY, NOT NULL |
| `time` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |
| `action` | CHAR(1) | NOT NULL, DEFAULT 'S' |

---

### Source History Extension

#### source_histories

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE CASCADE |
| `user_ip` | SCORE_INET | NOT NULL |
| `source` | TEXT | NOT NULL |
| `date_set` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |

**Indexes:**
- `source_histories_image_id_idx` on `image_id`

---

### Tag Categories Extension

#### image_tag_categories

| Column | Type | Constraints |
|--------|------|-------------|
| `category` | VARCHAR(60) | PRIMARY KEY |
| `display_singular` | VARCHAR(60) | |
| `display_multiple` | VARCHAR(60) | |
| `color` | VARCHAR(7) | |

---

### Tag History Extension

#### tag_histories

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `image_id` | INTEGER | NOT NULL, FK → images(id) ON DELETE CASCADE |
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE CASCADE |
| `user_ip` | SCORE_INET | NOT NULL |
| `tags` | TEXT | NOT NULL |
| `date_set` | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP |

**Indexes:**
- `tag_histories_image_id_idx` on `image_id`

---

### Tips Extension

#### tips

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `enable` | BOOLEAN | NOT NULL, DEFAULT FALSE |
| `image` | TEXT | NOT NULL |
| `text` | TEXT | NOT NULL |

---

### Tombstones Extension

#### tombstones

| Column | Type | Constraints |
|--------|------|-------------|
| `post_id` | INTEGER | NOT NULL |
| `hash` | CHAR(32) | NOT NULL |
| `date` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `message` | TEXT | NOT NULL |

---

### User Config Extension

#### user_config

| Column | Type | Constraints |
|--------|------|-------------|
| `user_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE CASCADE |
| `name` | VARCHAR(128) | NOT NULL |
| `value` | TEXT | |

**Primary Key:** (`user_id`, `name`)

**Indexes:**
- `user_config_user_id_idx` on `user_id`

---

### Wiki Extension

#### wiki_pages

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | SCORE_AIPK | PRIMARY KEY |
| `owner_id` | INTEGER | NOT NULL, FK → users(id) ON DELETE RESTRICT |
| `owner_ip` | SCORE_INET | NOT NULL |
| `date` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `title` | VARCHAR(255) | NOT NULL |
| `revision` | INTEGER | NOT NULL, DEFAULT 1 |
| `locked` | BOOLEAN | NOT NULL, DEFAULT FALSE |
| `body` | TEXT | NOT NULL |

**Indexes:**
- Index on (`title`, `revision`)

---

## Relationships

### Entity Relationship Diagram (Textual)

```
users (1) ─────┬──── (many) images
              ├──── (many) comments
              ├──── (many) forum_threads
              ├──── (many) forum_posts
              ├──── (many) pools
              ├──── (many) private_message (from_id)
              ├──── (many) private_message (to_id)
              ├──── (many) user_favorites
              ├──── (many) user_config
              ├──── (many) wiki_pages
              ├──── (many) bans (banner_id)
              ├──── (many) artists
              ├──── (many) notes
              ├──── (many) tag_histories
              └──── (many) source_histories

images (1) ────┬──── (many) image_tags
              ├──── (many) comments
              ├──── (many) user_favorites
              ├──── (many) pool_images
              ├──── (many) notes
              ├──── (many) image_reports
              ├──── (many) tag_histories
              ├──── (many) source_histories
              └──── (one)  image_descriptions

tags (1) ──────┴──── (many) image_tags

pools (1) ─────┬──── (many) pool_images
              └──── (many) pool_history

forum_threads (1) ── (many) forum_posts

artists (1) ───┬──── (many) artist_members
              ├──── (many) artist_alias
              └──── (many) artist_urls

notes (1) ─────┴──── (many) note_histories

user_classes (1) ─── (many) user_class_permissions
```

### Cascade Behavior

| Relationship | ON DELETE | ON UPDATE |
|--------------|-----------|-----------|
| images → users | RESTRICT | - |
| comments → images | CASCADE | - |
| comments → users | RESTRICT | - |
| image_tags → images | CASCADE | - |
| image_tags → tags | CASCADE | - |
| user_favorites → images | CASCADE | - |
| user_favorites → users | CASCADE | - |
| forum_posts → forum_threads | CASCADE | CASCADE |
| forum_posts → users | RESTRICT | CASCADE |
| pools → users | CASCADE | CASCADE |
| pool_images → pools | CASCADE | CASCADE |
| pool_images → images | CASCADE | CASCADE |
| notes → images | CASCADE | - |
| notes → users | CASCADE | CASCADE |
| private_message → users | CASCADE | - |
| wiki_pages → users | RESTRICT | - |

---

## Database Engine Support

Shimmie2 supports three database engines through its abstraction layer:

### MySQL / MariaDB
- Auto-increment: `INTEGER PRIMARY KEY AUTO_INCREMENT`
- IP addresses: `VARCHAR(45)` (supports IPv6)
- Boolean: `TINYINT(1)`

### PostgreSQL
- Auto-increment: `INTEGER NOT NULL PRIMARY KEY GENERATED ALWAYS AS IDENTITY`
- IP addresses: Native `INET` type
- Boolean: Native `BOOLEAN` type
- Additional: Function-based indexes (e.g., `LOWER(tag)`)

### SQLite
- Auto-increment: `INTEGER PRIMARY KEY AUTOINCREMENT`
- IP addresses: `VARCHAR(45)`
- Boolean: `INTEGER` (0/1)

---

## Schema Evolution

The database schema evolves through extension upgrade events. Each extension defines its schema changes in its `onDatabaseUpgrade()` method, which runs when the extension version changes. This allows for:

- Incremental schema updates
- Extension-specific tables
- Adding columns to core tables
- Creating indexes as needed

The schema version is tracked in the `config` table using keys like `ext_<extension_name>_version`.
