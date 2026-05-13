# Admin Panel Integration Generator

Generate frontend components to integrate an existing backend model into the admin control panel.

## Overview

This command generates all necessary frontend files to add a new entity to the admin control panel, following the exact patterns established in the Users category (AccountsLayout, RolesLayout, AbilitiesLayout).

## Usage

```
/admin-panel-integration {EntityName} [options]
```

### Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `EntityName` | Yes | PascalCase singular name (e.g., `Office`, `DocumentType`, `Level`) |

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `--category` | inferred | Admin category: `users`, `offices`, `documents`, `uacs`, `app-manager` |
| `--endpoint` | `admin/{entities}` | API endpoint override |
| `--relations` | none | Comma-separated relations (e.g., `roles,abilities,offices`) |
| `--no-create` | false | Skip CreateCard generation |
| `--no-edit` | false | Skip EditCard generation |
| `--no-picker` | false | Skip PickerModal generation |

## Prerequisites Checklist

Before generating, verify these prerequisites:

### 1. Backend API Endpoints Must Exist

```
GET    /api/admin/{entities}              # List (with with_trashed support)
GET    /api/admin/{entities}/{id}         # Show single item
POST   /api/admin/{entities}              # Create
PUT    /api/admin/{entities}/{id}         # Update
DELETE /api/admin/{entities}/{id}         # Soft delete
PATCH  /api/admin/{entities}/{id}/restore # Restore
```

### 2. For Each Relation (if `--relations` specified)

```
GET    /api/admin/{entities}/{id}/{relations}                    # List relations
POST   /api/admin/{entities}/{id}/{relations}/batch-assign       # Batch assign
DELETE /api/admin/{entities}/{id}/{relations}/batch-remove       # Batch remove
DELETE /api/admin/{entities}/{id}/{relations}/{relation_id}      # Remove single
```

### 3. Validation Steps

1. **Test API availability**: `curl -X GET /api/admin/{entities}` should return 200
2. **Check response structure**: Confirm `data` array with entity objects
3. **Verify soft-delete support**: Response should include `deleted_at` field
4. **Check relation endpoints**: Each relation endpoint should be accessible

---

## File Generation Order

Generate files in this order to satisfy dependencies:

1. `types/{entity}.type.ts` - TypeScript definitions (if missing)
2. `composables/queries/admin/admin.{entity}.query.ts` - Data fetching
3. `composables/mutations/admin/admin.{entity}.mutation.ts` - Data mutations
4. `components/admin/list-items/{Entity}ListItem.vue` - List item component
5. `layouts/app/admin/{category}/{entities}/partials/{Entity}ViewContent.vue` - View content
6. `layouts/app/admin/{category}/{entities}/partials/{Entity}{Relation}Section.vue` - Per relation
7. `components/local/app/admin/{category}/{entities}/Create{Entity}Card.vue` - Create form
8. `components/local/app/admin/{category}/{entities}/Edit{Entity}Card.vue` - Edit form
9. `layouts/app/admin/{category}/{entities}/{Entity}sLayout.vue` - Main layout
10. `pages/app/admin/{category}/{entities}/{Entity}sPage.vue` - Page wrapper
11. Update `components/admin/index.ts` - Export new list item

---

## Template: TypeScript Type Definition

**File**: `resources/js/types/{entity}.type.ts`

```typescript
/**
 * {Entity} Type Definitions
 *
 * Auto-generated from API response structure.
 * Customize as needed for your entity's specific fields.
 */

import { Id } from '@/types';

/**
 * {Entity} - Main entity interface
 */
export interface {Entity} {
  id: Id;
  // === PRIMARY FIELDS ===
  // Add entity-specific fields here based on API response
  name: string;
  slug?: string;
  description?: string;

  // === RELATION COUNTS ===
  // Add count fields for each relation
  // {relation}_count?: number;

  // === TIMESTAMPS ===
  created_at: string;
  updated_at: string;
  deleted_at?: string | null;
}

/**
 * {Entity} List Item - Optimized for list views
 */
export type {Entity}ListItem = Pick<{Entity}, 'id' | 'name' | 'deleted_at'> & {
  // Add only fields needed for list display
};

/**
 * {Entity} Form Data - For create/update forms
 */
export interface {Entity}FormData {
  name: string;
  // Add form fields (exclude id, timestamps, counts)
}
```

---

## Template: Query Composable

**File**: `resources/js/composables/queries/admin/admin.{entity}.query.ts`

```typescript
import { useApiClient } from '@/composables/utilities/useApiClient';
import { Id, QueryParams } from '@/types';
import { {Entity} } from '@/types/{entity}.type';
import { useQuery } from '@tanstack/vue-query';
import { computed, MaybeRefOrGetter, unref } from 'vue';

const endpoint = 'admin/{entities}';
const { apiRequest } = useApiClient();

/**
 * Fetch list of {entities} for admin panel
 *
 * @param params - Query parameters (with_trashed, etc.)
 * @param config - Additional axios config
 */
export const useAdmin{Entity}ListQuery = (params: QueryParams = {}, config: Record<string, any> = {}) => {
  const initialData: Array<{Entity}> = [];
  const queryKey = ['admin-{entities}'];
  const queryFn = () => {
    const _endpoint = endpoint;
    const _params = {
      // Include common eager loads
      // with_{relation}: 1, // true
      // count_{relation}: 1, // true
      ...params,
    };
    const _config = { ...config, params: _params };
    return apiRequest(_endpoint, _config);
  };
  const select = (e: any) => (e?.data ? e.data : initialData);

  const { data, error, isError, isFetching, isLoading, isPending, refetch, status } = useQuery({
    queryKey,
    queryFn,
    initialData,
    select,
  });

  return {
    data,
    error,
    isError,
    isFetching,
    isLoading,
    isPending,
    refetch,
    status,
  };
};

/**
 * Fetch single {entity} with full details
 *
 * @param id - {Entity} ID (reactive)
 * @param params - Query parameters
 * @param config - Additional axios config
 */
export const useAdmin{Entity}Query = (
  id: MaybeRefOrGetter<Id | null | undefined>,
  params: QueryParams = {},
  config: Record<string, any> = {}
) => {
  const initialData: {Entity} = {} as {Entity};
  const queryKey = computed(() => ['admin-{entities}', unref(id), 'enhanced']);
  const queryFn = () => {
    const _id = unref(id);
    const _endpoint = `${endpoint}/${_id}`;
    const _params = {
      // Include relations and counts for detail view
      // with_{relation}: 1, // true
      // count_{relation}: 1, // true
      ...params,
    };
    const _config = { ...config, params: _params };
    return apiRequest(_endpoint, _config);
  };
  const select = (e: any) => (e?.data ? e.data : initialData);

  const { data, error, isError, isFetching, isLoading, isPending, refetch, status } = useQuery({
    queryKey,
    queryFn,
    enabled: computed(() => !!unref(id)),
    placeholderData: initialData,
    select,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });

  return {
    data,
    error,
    isError,
    isFetching,
    isLoading,
    isPending,
    refetch,
    status,
  };
};

// ============================================================================
// RELATION QUERIES (Add per relation specified in --relations)
// ============================================================================

/**
 * Template for relation query - duplicate for each relation
 *
 * Fetch {relation}s for a specific {entity}
 * Lazy-loaded: only fetches when enabled (tab is active)
 */
/*
export const use{Entity}{Relation}sQuery = (
  {entity}Id: MaybeRefOrGetter<Id | null | undefined>,
  params: QueryParams = {},
  config: { enabled?: MaybeRefOrGetter<boolean> } & Record<string, any> = {},
) => {
  const initialData: Array<{Relation}> = [];
  const queryKey = computed(() => ['admin-{entities}', unref({entity}Id), '{relations}']);
  const queryFn = () => {
    const _id = unref({entity}Id);
    const _endpoint = `${endpoint}/${_id}/{relations}`;
    const _params = { ...params };
    const _config = { ...config, params: _params };
    return apiRequest(_endpoint, _config);
  };
  const select = (e: any) => (e?.data ? e.data : initialData);

  const { data, error, isError, isFetching, isLoading, isPending, refetch, status } = useQuery({
    queryKey,
    queryFn,
    enabled: computed(() => !!unref({entity}Id) && (unref(config.enabled) ?? true)),
    placeholderData: initialData,
    select,
    staleTime: 1000 * 60 * 5,
  });

  return {
    data,
    error,
    isError,
    isFetching,
    isLoading,
    isPending,
    refetch,
    status,
  };
};
*/
```

---

## Template: Mutation Composable

**File**: `resources/js/composables/mutations/admin/admin.{entity}.mutation.ts`

```typescript
import { useApiClient } from '@/composables/utilities/useApiClient';
import { Id } from '@/types';
import { useMutation, useQueryClient } from '@tanstack/vue-query';
import axios from 'axios';
import { ref } from 'vue';

const endpoint = 'admin/{entities}';
const { apiRequest } = useApiClient();

// ============================================================================
// CRUD MUTATIONS
// ============================================================================

/**
 * Create a new {entity}
 */
export const useAdminCreate{Entity}Mutation = (
  onSuccessCallback: (() => void) | null = null,
  config: Record<string, any> = {}
) => {
  const queryClient = useQueryClient();
  const mutationFn = (payload: {
    name: string;
    // Add create fields based on entity
  }) => {
    const body = { ...payload };
    const _endpoint = endpoint;
    const _config = { ...config, body };
    return apiRequest.post(_endpoint, _config);
  };

  const validationErrors = ref<Record<string, string[]>>({});
  const onError = (error: any, variables: any, context: any) => {
    if (axios.isAxiosError(error)) {
      if (error.status === 422) {
        validationErrors.value = error.response?.data.errors;
      } else {
        console.error('KRIS - useAdminCreate{Entity}Mutation - error:', error);
      }
    }
  };
  const onMutate = () => {
    return { data: 'Mutation initiated' };
  };
  const onSuccess = () => {
    if (onSuccessCallback) onSuccessCallback();
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}'] });
  };

  const { data, error, isError, isPending, mutate, mutateAsync, status } = useMutation({
    mutationFn,
    onError,
    onMutate,
    onSettled: () => {},
    onSuccess,
  });

  return {
    data,
    error,
    isError,
    isPending,
    mutate,
    mutateAsync,
    status,
    validationErrors,
  };
};

/**
 * Update an existing {entity}
 */
export const useAdminUpdate{Entity}Mutation = (
  onSuccessCallback: (() => void) | null = null,
  config: Record<string, any> = {}
) => {
  const queryClient = useQueryClient();
  const mutationFn = (payload: {
    id: Id;
    name?: string;
    // Add update fields
  }) => {
    const { id, ...body } = payload;
    const _endpoint = `${endpoint}/${id}`;
    const _config = { ...config, body };
    return apiRequest.put(_endpoint, _config);
  };

  const validationErrors = ref<Record<string, string[]>>({});
  const onError = (error: any) => {
    if (axios.isAxiosError(error) && error.status === 422) {
      validationErrors.value = error.response?.data.errors;
    }
  };
  const onSuccess = () => {
    if (onSuccessCallback) onSuccessCallback();
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}'] });
  };

  const { data, error, isError, isPending, mutate, mutateAsync, status } = useMutation({
    mutationFn,
    onError,
    onMutate: () => ({ data: 'Mutation initiated' }),
    onSettled: () => {},
    onSuccess,
  });

  return { data, error, isError, isPending, mutate, mutateAsync, status, validationErrors };
};

/**
 * Soft delete a {entity}
 */
export const useAdminDelete{Entity}Mutation = (
  onSuccessCallback: (() => void) | null = null,
  config: Record<string, any> = {}
) => {
  const queryClient = useQueryClient();
  const mutationFn = (payload: { id: Id }) => {
    const _endpoint = `${endpoint}/${payload.id}`;
    return apiRequest.delete(_endpoint, config);
  };

  const validationErrors = ref<Record<string, string[]>>({});
  const onError = (error: any) => {
    if (axios.isAxiosError(error) && error.status === 422) {
      validationErrors.value = error.response?.data.errors;
    }
  };
  const onSuccess = () => {
    if (onSuccessCallback) onSuccessCallback();
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}'] });
  };

  const { data, error, isError, isPending, mutate, mutateAsync, status } = useMutation({
    mutationFn,
    onError,
    onMutate: () => ({ data: 'Mutation initiated' }),
    onSettled: () => {},
    onSuccess,
  });

  return { data, error, isError, isPending, mutate, mutateAsync, status, validationErrors };
};

/**
 * Restore a soft-deleted {entity}
 */
export const useAdminRestore{Entity}Mutation = (
  onSuccessCallback: (() => void) | null = null,
  config: Record<string, any> = {}
) => {
  const queryClient = useQueryClient();
  const mutationFn = (payload: { id: Id }) => {
    const _endpoint = `${endpoint}/${payload.id}/restore`;
    return apiRequest.patch(_endpoint, config);
  };

  const validationErrors = ref<Record<string, string[]>>({});
  const onError = (error: any) => {
    if (axios.isAxiosError(error) && error.status === 422) {
      validationErrors.value = error.response?.data.errors;
    }
  };
  const onSuccess = () => {
    if (onSuccessCallback) onSuccessCallback();
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}'] });
  };

  const { data, error, isError, isPending, mutate, mutateAsync, status } = useMutation({
    mutationFn,
    onError,
    onMutate: () => ({ data: 'Mutation initiated' }),
    onSettled: () => {},
    onSuccess,
  });

  return { data, error, isError, isPending, mutate, mutateAsync, status, validationErrors };
};

// ============================================================================
// RELATION MUTATIONS (Add per relation specified in --relations)
// ============================================================================

/**
 * Template for batch assign relation mutation
 */
/*
export const useAdminBatchAssign{Relation}sTo{Entity}Mutation = (
  onSuccessCallback: (() => void) | null = null,
  config: Record<string, any> = {}
) => {
  const queryClient = useQueryClient();
  const mutationFn = (payload: { {entity}_id: Id; {relation}_ids: Id[] }) => {
    const body = { {relation}_ids: payload.{relation}_ids };
    const _endpoint = `${endpoint}/${payload.{entity}_id}/{relations}/batch-assign`;
    const _config = { ...config, body };
    return apiRequest.post(_endpoint, _config);
  };

  const validationErrors = ref<Record<string, string[]>>({});
  const onError = (error: any) => {
    if (axios.isAxiosError(error) && error.status === 422) {
      validationErrors.value = error.response?.data.errors;
    }
  };
  const onSuccess = () => {
    if (onSuccessCallback) onSuccessCallback();
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}'], refetchType: 'all' });
  };

  const { data, error, isError, isPending, mutate, mutateAsync, status } = useMutation({
    mutationFn,
    onError,
    onMutate: () => ({ data: 'Mutation initiated' }),
    onSettled: () => {},
    onSuccess,
  });

  return { data, error, isError, isPending, mutate, mutateAsync, status, validationErrors };
};
*/

/**
 * Template for remove single relation with OPTIMISTIC UPDATES
 */
/*
export const useAdminRemove{Relation}From{Entity}Mutation = (
  onSuccessCallback: (() => void) | null = null,
  config: Record<string, any> = {}
) => {
  const queryClient = useQueryClient();

  type MutationContext = {
    previous{Entity}{Relation}s: any[] | undefined;
    removed{Relation}Id: Id;
  };

  const mutationFn = (payload: { {entity}_id: Id; {relation}_id: Id }) => {
    const _endpoint = `${endpoint}/${payload.{entity}_id}/{relations}/${payload.{relation}_id}`;
    return apiRequest.delete(_endpoint, config);
  };

  const validationErrors = ref<Record<string, string[]>>({});

  // OPTIMISTIC UPDATE - Fires BEFORE the API call
  const onMutate = async (variables: { {entity}_id: Id; {relation}_id: Id }): Promise<MutationContext> => {
    await queryClient.cancelQueries({ queryKey: ['admin-{entities}', variables.{entity}_id, '{relations}'] });
    const previous{Entity}{Relation}s = queryClient.getQueryData(['admin-{entities}', variables.{entity}_id, '{relations}']) as any[] | undefined;

    queryClient.setQueryData(['admin-{entities}', variables.{entity}_id, '{relations}'], (old: any) => {
      if (!old || !Array.isArray(old)) return old;
      return old.filter((item: any) => item.id !== variables.{relation}_id);
    });

    return { previous{Entity}{Relation}s, removed{Relation}Id: variables.{relation}_id };
  };

  // ROLLBACK on error
  const onError = (error: any, variables: { {entity}_id: Id; {relation}_id: Id }, context: MutationContext | undefined) => {
    if (context?.previous{Entity}{Relation}s) {
      queryClient.setQueryData(['admin-{entities}', variables.{entity}_id, '{relations}'], context.previous{Entity}{Relation}s);
    }
    if (axios.isAxiosError(error) && error.status === 422) {
      validationErrors.value = error.response?.data.errors;
    }
  };

  const onSuccess = (_: any, variables: { {entity}_id: Id; {relation}_id: Id }) => {
    if (onSuccessCallback) onSuccessCallback();
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}', variables.{entity}_id, 'enhanced'] });
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}'] });
  };

  const { data, error, isError, isPending, mutate, mutateAsync, status } = useMutation({
    mutationFn,
    onError,
    onMutate,
    onSettled: () => {},
    onSuccess,
  });

  return { data, error, isError, isPending, mutate, mutateAsync, status, validationErrors };
};
*/

/**
 * Template for batch remove relations with OPTIMISTIC UPDATES
 */
/*
export const useAdminBatchRemove{Relation}sFrom{Entity}Mutation = (
  onSuccessCallback: (() => void) | null = null,
  config: Record<string, any> = {}
) => {
  const queryClient = useQueryClient();

  type MutationContext = {
    previous{Entity}{Relation}s: any[] | undefined;
    removed{Relation}Ids: Id[];
  };

  const mutationFn = (payload: { {entity}_id: Id; {relation}_ids: Id[] }) => {
    const body = { {relation}_ids: payload.{relation}_ids };
    const _endpoint = `${endpoint}/${payload.{entity}_id}/{relations}/batch-remove`;
    const _config = { ...config, body };
    return apiRequest.delete(_endpoint, _config);
  };

  const validationErrors = ref<Record<string, string[]>>({});

  const onMutate = async (variables: { {entity}_id: Id; {relation}_ids: Id[] }): Promise<MutationContext> => {
    await queryClient.cancelQueries({ queryKey: ['admin-{entities}', variables.{entity}_id, '{relations}'] });
    const previous{Entity}{Relation}s = queryClient.getQueryData(['admin-{entities}', variables.{entity}_id, '{relations}']) as any[] | undefined;

    queryClient.setQueryData(['admin-{entities}', variables.{entity}_id, '{relations}'], (old: any) => {
      if (!old || !Array.isArray(old)) return old;
      return old.filter((item: any) => !variables.{relation}_ids.includes(item.id));
    });

    return { previous{Entity}{Relation}s, removed{Relation}Ids: variables.{relation}_ids };
  };

  const onError = (error: any, variables: { {entity}_id: Id; {relation}_ids: Id[] }, context: MutationContext | undefined) => {
    if (context?.previous{Entity}{Relation}s) {
      queryClient.setQueryData(['admin-{entities}', variables.{entity}_id, '{relations}'], context.previous{Entity}{Relation}s);
    }
    if (axios.isAxiosError(error) && error.status === 422) {
      validationErrors.value = error.response?.data.errors;
    }
  };

  const onSuccess = (_: any, variables: { {entity}_id: Id; {relation}_ids: Id[] }) => {
    if (onSuccessCallback) onSuccessCallback();
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}', variables.{entity}_id, 'enhanced'] });
    queryClient.invalidateQueries({ queryKey: ['admin-{entities}'] });
  };

  const { data, error, isError, isPending, mutate, mutateAsync, status } = useMutation({
    mutationFn,
    onError,
    onMutate,
    onSettled: () => {},
    onSuccess,
  });

  return { data, error, isError, isPending, mutate, mutateAsync, status, validationErrors };
};
*/
```

---

## Template: List Item Component

**File**: `resources/js/components/admin/list-items/{Entity}ListItem.vue`

Uses the `AdminListItem` base component for consistent styling. The base component provides:
- Configurable `title-field` and `subtitle-field` props
- Optional subtitle via `:show-subtitle="false"`
- Automatic deleted state handling
- Slots for `#badges-top`, `#icon`, `#badges-bottom`
- Theme-aware styling with light/dark mode support

```vue
<script setup lang="ts">
/**
 * {Entity}ListItem - List item for admin selector panel
 *
 * Displays {entity} in the left panel list with:
 * - Primary text (name/title)
 * - Optional subtitle (slug/description)
 * - Status badges (active/deleted)
 * - Optional bottom badges (counts, metadata)
 *
 * Uses AdminListItem base component for consistent styling.
 */
import { computed } from 'vue';
import { AdminListItem } from '@/components/admin';
import KBadge from '@/components/k/badge/KBadge.vue';
import { {Entity} } from '@/types/{entity}.type';

interface Props {
  {entity}: {Entity};
}

const props = defineProps<Props>();

const isDeleted = computed(() => !!props.{entity}.deleted_at);
// Add computed properties for counts, etc.
// const relationCount = computed(() => props.{entity}.{relation}_count || 0);
</script>

<template>
  <AdminListItem
    :item="{entity}"
    title-field="name"
    subtitle-field="description"
    :deleted="isDeleted"
  >
    <!-- Top-right badges: status indicators -->
    <template #badges-top>
      <k-badge v-if="!isDeleted" size="xs" color="success">active</k-badge>
      <k-badge v-else size="xs" color="error">deleted</k-badge>
    </template>

    <!-- Icon slot (optional) -->
    <template #icon>
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="none" viewBox="0 0 24 24" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z" />
      </svg>
    </template>

    <!-- Bottom badges: counts, metadata -->
    <template #badges-bottom>
      <!-- Add count badges as needed -->
      <!--
      <k-badge
        size="xs"
        :color="relationCount > 0 ? 'primary' : 'gray'"
        variant="soft"
      >
        {{ relationCount }} {{ relationCount === 1 ? '{relation}' : '{relations}' }}
      </k-badge>
      -->
    </template>
  </AdminListItem>
</template>

<style scoped>
/* Add entity-specific styles here if needed */
/* Most styling is handled by AdminListItem base component */
</style>
```

### AdminListItem Props Reference

| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `item` | `Record<string, any>` | required | The data item to display |
| `title-field` | `string` | `'name'` or `'title'` | Field name for the title |
| `subtitle-field` | `string` | `'slug'` or `'description'` | Field name for the subtitle |
| `show-subtitle` | `boolean` | `true` | Whether to show the subtitle |
| `show-icon` | `boolean` | `true` | Whether to show the icon slot area |
| `deleted` | `boolean` | `false` | Whether item is soft-deleted |
| `mono-title` | `boolean` | `false` | Use monospace font for title (for codes) |

### AdminListItem Slots

| Slot | Scope Props | Description |
|------|-------------|-------------|
| `badges-top` | `{ item, deleted }` | Top-right corner badges (status) |
| `icon` | `{ item, deleted }` | Left icon area |
| `title` | `{ item, title }` | Override title content |
| `subtitle` | `{ item, subtitle }` | Override subtitle content |
| `badges-bottom` | `{ item, deleted }` | Bottom badges (counts, metadata) |

---

## Template: View Content Partial

**File**: `resources/js/layouts/app/admin/{category}/{entities}/partials/{Entity}ViewContent.vue`

```vue
<script setup lang="ts">
/**
 * {Entity}ViewContent - View mode content for detail panel
 *
 * Displays {entity} details in a clean, organized layout.
 */
import { computed } from 'vue';
import { {Entity} } from '@/types/{entity}.type';

interface Props {
  {entity}: {Entity};
}

const props = defineProps<Props>();

// Format date helper
const formatDate = (date: string | null | undefined) => {
  if (!date) return 'N/A';
  return new Date(date).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
};
</script>

<template>
  <div class="{entity}-view-content">
    <!-- Basic Information -->
    <div class="view-section">
      <h3 class="view-section__title">Basic Information</h3>
      <div class="view-grid">
        <div class="view-field">
          <label class="view-field__label">Name</label>
          <span class="view-field__value">{{ {entity}.name || 'N/A' }}</span>
        </div>
        <!-- Add more fields as needed -->
        <!--
        <div class="view-field">
          <label class="view-field__label">Slug</label>
          <span class="view-field__value view-field__value--code">{{ {entity}.slug || 'N/A' }}</span>
        </div>
        <div class="view-field view-field--full">
          <label class="view-field__label">Description</label>
          <span class="view-field__value">{{ {entity}.description || 'No description' }}</span>
        </div>
        -->
      </div>
    </div>

    <!-- Timestamps -->
    <div class="view-section">
      <h3 class="view-section__title">Timestamps</h3>
      <div class="view-grid">
        <div class="view-field">
          <label class="view-field__label">Created</label>
          <span class="view-field__value">{{ formatDate({entity}.created_at) }}</span>
        </div>
        <div class="view-field">
          <label class="view-field__label">Updated</label>
          <span class="view-field__value">{{ formatDate({entity}.updated_at) }}</span>
        </div>
        <div v-if="{entity}.deleted_at" class="view-field">
          <label class="view-field__label">Deleted</label>
          <span class="view-field__value view-field__value--danger">
            {{ formatDate({entity}.deleted_at) }}
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.{entity}-view-content {
  padding: 24px;
}

.view-section {
  margin-bottom: 32px;
}

.view-section:last-child {
  margin-bottom: 0;
}

.view-section__title {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: rgba(var(--v-theme-on-surface), 0.5);
  margin: 0 0 16px 0;
}

.view-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 16px;
}

.view-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.view-field--full {
  grid-column: 1 / -1;
}

.view-field__label {
  font-size: 0.75rem;
  font-weight: 500;
  color: rgba(var(--v-theme-on-surface), 0.5);
}

.view-field__value {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-surface), 0.87);
}

.view-field__value--code {
  font-family: 'Monaco', 'Consolas', monospace;
  font-size: 0.8125rem;
  background: rgba(var(--v-theme-on-surface), 0.04);
  padding: 4px 8px;
  border-radius: 4px;
  display: inline-block;
}

.view-field__value--danger {
  color: rgb(var(--v-theme-error));
}
</style>
```

---

## Template: Relation Section Partial

**File**: `resources/js/layouts/app/admin/{category}/{entities}/partials/{Entity}{Relation}sSection.vue`

```vue
<script setup lang="ts">
/**
 * {Entity}{Relation}sSection - {Relation}s management tab
 *
 * Displays and manages {relations} assigned to this {entity}.
 * Supports:
 * - Add {relations} via picker modal
 * - Remove single {relation}
 * - Bulk remove selected {relations}
 */
import { ref, computed } from 'vue';
import { Id } from '@/types';
import KButton from '@/components/k/form/KButton.vue';
import {Relation}ListItem from '@/components/admin/list-items/{Relation}ListItem.vue';

interface Props {
  {entity}Id: Id;
  {relations}: any[];
  loading?: boolean;
  {relations}Count?: number;
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
  {relations}Count: 0,
});

const emit = defineEmits<{
  add: [];
  remove: [id: Id, name: string];
  'bulk-remove': [ids: Id[]];
}>();

// Selection state for bulk operations
const selectedIds = ref<Set<Id>>(new Set());
const isSelectMode = ref(false);

const selectedCount = computed(() => selectedIds.value.size);

function toggleSelect(id: Id) {
  if (selectedIds.value.has(id)) {
    selectedIds.value.delete(id);
  } else {
    selectedIds.value.add(id);
  }
  selectedIds.value = new Set(selectedIds.value); // Trigger reactivity
}

function selectAll() {
  props.{relations}.forEach((item: any) => selectedIds.value.add(item.id));
  selectedIds.value = new Set(selectedIds.value);
}

function clearSelection() {
  selectedIds.value.clear();
  selectedIds.value = new Set(selectedIds.value);
  isSelectMode.value = false;
}

function handleBulkRemove() {
  emit('bulk-remove', Array.from(selectedIds.value));
  clearSelection();
}
</script>

<template>
  <div class="{entity}-{relations}-section">
    <!-- Section Header -->
    <div class="section-header">
      <div class="section-header__left">
        <h3 class="section-header__title">
          {Relation}s
          <span class="section-header__count">({{ {relations}Count || {relations}.length }})</span>
        </h3>
      </div>
      <div class="section-header__actions">
        <template v-if="isSelectMode">
          <KButton
            variant="ghost"
            size="sm"
            @click="selectAll"
          >
            Select All
          </KButton>
          <KButton
            variant="danger"
            size="sm"
            :disabled="selectedCount === 0"
            @click="handleBulkRemove"
          >
            Remove ({{ selectedCount }})
          </KButton>
          <KButton
            variant="ghost"
            size="sm"
            @click="clearSelection"
          >
            Cancel
          </KButton>
        </template>
        <template v-else>
          <KButton
            variant="ghost"
            size="sm"
            icon="mdi-checkbox-multiple-outline"
            title="Select multiple"
            @click="isSelectMode = true"
          />
          <KButton
            variant="primary"
            size="sm"
            icon="mdi-plus"
            @click="emit('add')"
          >
            Add {Relation}
          </KButton>
        </template>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="section-loading">
      <div class="skeleton-list">
        <div v-for="i in 3" :key="i" class="skeleton-item" />
      </div>
    </div>

    <!-- Empty State -->
    <div v-else-if="{relations}.length === 0" class="section-empty">
      <p>No {relations} assigned yet.</p>
      <KButton variant="primary" size="sm" @click="emit('add')">
        Add {Relation}
      </KButton>
    </div>

    <!-- {Relation}s List -->
    <div v-else class="section-list">
      <div
        v-for="{relation} in {relations}"
        :key="{relation}.id"
        class="section-list__item"
        :class="{ 'section-list__item--selected': selectedIds.has({relation}.id) }"
        @click="isSelectMode ? toggleSelect({relation}.id) : null"
      >
        <div v-if="isSelectMode" class="section-list__checkbox">
          <input
            type="checkbox"
            :checked="selectedIds.has({relation}.id)"
            @change="toggleSelect({relation}.id)"
            @click.stop
          />
        </div>
        <div class="section-list__content">
          <{Relation}ListItem :{relation}="{relation}" />
        </div>
        <div v-if="!isSelectMode" class="section-list__actions">
          <KButton
            variant="ghost"
            size="sm"
            icon="mdi-close"
            title="Remove"
            @click.stop="emit('remove', {relation}.id, {relation}.name)"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.{entity}-{relations}-section {
  padding: 16px 0;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px 16px;
  border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.08);
}

.section-header__title {
  font-size: 0.875rem;
  font-weight: 600;
  margin: 0;
  color: rgba(var(--v-theme-on-surface), 0.87);
}

.section-header__count {
  font-weight: 400;
  color: rgba(var(--v-theme-on-surface), 0.5);
}

.section-header__actions {
  display: flex;
  gap: 8px;
}

.section-loading,
.section-empty {
  padding: 24px;
  text-align: center;
  color: rgba(var(--v-theme-on-surface), 0.5);
}

.skeleton-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.skeleton-item {
  height: 48px;
  background: rgba(var(--v-theme-on-surface), 0.04);
  border-radius: 8px;
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}

.section-list__item {
  display: flex;
  align-items: center;
  padding: 8px 24px;
  transition: background-color 0.15s ease;
}

.section-list__item:hover {
  background: rgba(var(--v-theme-on-surface), 0.02);
}

.section-list__item--selected {
  background: rgba(var(--v-theme-primary), 0.04);
}

.section-list__checkbox {
  margin-right: 12px;
}

.section-list__content {
  flex: 1;
  min-width: 0;
}

.section-list__actions {
  opacity: 0;
  transition: opacity 0.15s ease;
}

.section-list__item:hover .section-list__actions {
  opacity: 1;
}
</style>
```

---

## Template: Main Layout

**File**: `resources/js/layouts/app/admin/{category}/{entities}/{Entity}sLayout.vue`

```vue
<script setup lang="ts">
/**
 * {Entity}sLayout - Admin panel layout for {entities}
 *
 * Follows the pattern from AccountsLayout and RolesLayout.
 * Uses AdminTwoPanelLayout, AdminSelectorPanel, AdminDetailPanel.
 *
 * Features:
 * - {Entity} CRUD operations
 * - Soft delete / restore
 * - Relation management (if applicable)
 */
import { computed, inject, ref, watch, type Ref } from 'vue';
import { Id } from '@/types';

// ============================================================================
// COMPOSABLES
// ============================================================================

import {
  useAdminSortFilter,
  useAdminPagination,
  useAdminDialogs,
  type SortOption,
} from '@/composables/admin';
import {
  useAdmin{Entity}ListQuery,
  useAdmin{Entity}Query,
  // Import relation queries if applicable
  // use{Entity}{Relation}sQuery,
} from '@/composables/queries/admin/admin.{entity}.query';
import {
  useAdminDelete{Entity}Mutation,
  useAdminRestore{Entity}Mutation,
  // Import relation mutations if applicable
  // useAdminBatchAssign{Relation}sTo{Entity}Mutation,
  // useAdminRemove{Relation}From{Entity}Mutation,
  // useAdminBatchRemove{Relation}sFrom{Entity}Mutation,
} from '@/composables/mutations/admin/admin.{entity}.mutation';
import { useToast } from '@/toast-system/composables/useToast';

// ============================================================================
// COMPONENTS
// ============================================================================

import {
  AdminTwoPanelLayout,
  AdminSelectorPanel,
  AdminDetailPanel,
  {Entity}ListItem,
  // Import picker modals for relations if applicable
  // {Relation}PickerModal,
} from '@/components/admin';
import {Entity}ViewContent from './partials/{Entity}ViewContent.vue';
// Import relation sections if applicable
// import {Entity}{Relation}sSection from './partials/{Entity}{Relation}sSection.vue';
import PremiumConfirmDialog from '@/components/k_vuetify/dialog/PremiumConfirmDialog.vue';
import KButton from '@/components/k/form/KButton.vue';
import Create{Entity}Card from '@/components/local/app/admin/{category}/{entities}/Create{Entity}Card.vue';
import Edit{Entity}Card from '@/components/local/app/admin/{category}/{entities}/Edit{Entity}Card.vue';

// ============================================================================
// INJECT
// ============================================================================

const isSelectorPinned = inject<Ref<boolean>>('adminSelectorPinned', ref(false));

// ============================================================================
// DATA QUERIES
// ============================================================================

const {entity}ListQuery = useAdmin{Entity}ListQuery({ with_trashed: 1 }); // true
const {entity}List = computed(() => {entity}ListQuery.data.value || []);

// ============================================================================
// SORT/FILTER (using composable)
// ============================================================================

const sortOptions: SortOption[] = [
  { value: 'name-asc', label: 'Name (A-Z)', icon: 'mdi-sort-alphabetical-ascending' },
  { value: 'name-desc', label: 'Name (Z-A)', icon: 'mdi-sort-alphabetical-descending' },
  { value: 'created-desc', label: 'Newest first', icon: 'mdi-sort-calendar-descending' },
  { value: 'created-asc', label: 'Oldest first', icon: 'mdi-sort-calendar-ascending' },
  // Add entity-specific sort options
];

const { searchQuery, sortBy, sortedItems } = useAdminSortFilter({
  items: {entity}List,
  sortOptions,
  searchFields: ['name'], // Add searchable fields
});

// ============================================================================
// PAGINATION (using composable)
// ============================================================================

const {
  currentPage,
  itemsPerPage,
  itemsPerPageOptions,
  totalPages,
  paginatedItems,
  paginationInfo,
  visiblePageNumbers,
  isFirstPage,
  isLastPage,
  prevPage,
  nextPage,
} = useAdminPagination({
  items: sortedItems,
  defaultItemsPerPage: 10,
});

// Reset pagination when search changes
watch(searchQuery, () => {
  currentPage.value = 1;
});

// ============================================================================
// SELECTION STATE
// ============================================================================

const selectedItemId = ref<Id | null>(null);
const mode = ref<'view' | 'edit' | 'create'>('view');

// Fetch detailed data when selected
const {entity}DetailQuery = useAdmin{Entity}Query(selectedItemId);
const selected{Entity} = computed(() => {
  const data = {entity}DetailQuery.data.value;
  // Check for empty object (query returns {} as placeholder when no id)
  // This ensures the empty state panel shows when no item is selected
  return data && Object.keys(data).length > 0 ? data : null;
});
const is{Entity}DetailLoading = computed(() => {entity}DetailQuery.isFetching.value);

// ============================================================================
// SECTION TABS (if entity has relations)
// ============================================================================

// Uncomment and customize if entity has relations:
/*
const currentSection = ref<'{relation1}' | '{relation2}'>('relation1');

// Lazy-load: only fetch when tab is active
const is{Relation1}TabActive = computed(() => currentSection.value === '{relation1}');
const {entity}{Relation1}sQuery = use{Entity}{Relation1}sQuery(selectedItemId, {}, { enabled: is{Relation1}TabActive });
const {entity}{Relation1}s = computed(() => {entity}{Relation1}sQuery.data.value || []);
const is{Relation1}sLoading = computed(() => {entity}{Relation1}sQuery.isFetching.value);

const excluded{Relation1}Ids = computed(() => {
  return {entity}{Relation1}s.value.map((r: any) => r.id);
});
*/

// ============================================================================
// DIALOGS (using composable)
// ============================================================================

const dialogs = useAdminDialogs();
const delete{Entity}Dialog = dialogs.createDialog('delete{Entity}');
const restore{Entity}Dialog = dialogs.createDialog('restore{Entity}');
// Add relation dialogs if applicable:
// const remove{Relation}Dialog = dialogs.createDialog<{ id: Id; name: string }>('remove{Relation}');

// Picker modal states (if applicable):
// const show{Relation}Picker = ref(false);

// ============================================================================
// MUTATIONS
// ============================================================================

const toast = useToast();

const delete{Entity}Mutation = useAdminDelete{Entity}Mutation(() => {
  mode.value = 'view';
});
const restore{Entity}Mutation = useAdminRestore{Entity}Mutation();

// Add relation mutations if applicable:
// const batchAssign{Relation}sMutation = useAdminBatchAssign{Relation}sTo{Entity}Mutation();
// const remove{Relation}Mutation = useAdminRemove{Relation}From{Entity}Mutation();
// const batchRemove{Relation}sMutation = useAdminBatchRemove{Relation}sFrom{Entity}Mutation();

// ============================================================================
// HANDLERS
// ============================================================================

function handleItemSelect(item: any) {
  selectedItemId.value = item.id;
  mode.value = 'view';
}

function handleCreate() {
  selectedItemId.value = null;
  mode.value = 'create';
}

function handleEdit() {
  mode.value = 'edit';
}

function handleEditCancel() {
  mode.value = 'view';
}

function handleEditSuccess() {
  mode.value = 'view';
}

function handleCreateSuccess(id: number) {
  selectedItemId.value = id;
  mode.value = 'view';
}

function handleDelete{Entity}() {
  if (!selectedItemId.value) return;
  dialogs.setLoading('delete{Entity}', true);

  delete{Entity}Mutation.mutate(
    { id: selectedItemId.value },
    {
      onSuccess: () => {
        toast.success(`{Entity} '${selected{Entity}.value?.name}' deleted`);
        dialogs.closeDialog('delete{Entity}');
      },
      onError: () => {
        toast.error('Failed to delete {entity}');
        dialogs.setLoading('delete{Entity}', false);
      },
    }
  );
}

function handleRestore{Entity}() {
  if (!selectedItemId.value) return;
  dialogs.setLoading('restore{Entity}', true);

  restore{Entity}Mutation.mutate(
    { id: selectedItemId.value },
    {
      onSuccess: () => {
        toast.success(`{Entity} '${selected{Entity}.value?.name}' restored`);
        dialogs.closeDialog('restore{Entity}');
      },
      onError: () => {
        toast.error('Failed to restore {entity}');
        dialogs.setLoading('restore{Entity}', false);
      },
    }
  );
}

// Add relation handlers if applicable:
/*
function handleAdd{Relation}s({relation}Ids: Id[]) {
  if (!selectedItemId.value || {relation}Ids.length === 0) return;

  batchAssign{Relation}sMutation.mutate(
    { {entity}_id: selectedItemId.value, {relation}_ids: {relation}Ids },
    {
      onSuccess: () => {
        toast.success(`${{{relation}Ids.length}} {relation}(s) assigned`);
        show{Relation}Picker.value = false;
      },
    }
  );
}

function handleRemove{Relation}() {
  const data = remove{Relation}Dialog.data.value;
  if (!data?.id || !selectedItemId.value) return;
  dialogs.setLoading('remove{Relation}', true);

  remove{Relation}Mutation.mutate(
    { {entity}_id: selectedItemId.value, {relation}_id: data.id },
    {
      onSuccess: () => {
        toast.success(`{Relation} '${data.name}' removed`);
        dialogs.closeDialog('remove{Relation}');
      },
      onError: () => {
        toast.error('Failed to remove {relation}');
        dialogs.setLoading('remove{Relation}', false);
      },
    }
  );
}

function handleBulkRemove{Relation}s(ids: Id[]) {
  if (!selectedItemId.value || ids.length === 0) return;

  batchRemove{Relation}sMutation.mutate(
    { {entity}_id: selectedItemId.value, {relation}_ids: ids },
    {
      onSuccess: () => {
        toast.success(`${ids.length} {relation}(s) removed`);
      },
      onError: () => {
        toast.error('Failed to remove {relations}');
      },
    }
  );
}
*/
</script>

<template>
  <AdminTwoPanelLayout>
    <!-- Left Panel: {Entity} Selector -->
    <template #selector>
      <AdminSelectorPanel
        v-model:selected-id="selectedItemId"
        v-model:search="searchQuery"
        v-model:sort="sortBy"
        v-model:page="currentPage"
        v-model:items-per-page="itemsPerPage"
        :items="paginatedItems"
        :loading="{entity}ListQuery.isLoading.value"
        :sort-options="sortOptions"
        :total-pages="totalPages"
        :pagination-info="paginationInfo"
        :visible-pages="visiblePageNumbers"
        :items-per-page-options="itemsPerPageOptions"
        :is-first-page="isFirstPage"
        :is-last-page="isLastPage"
        search-placeholder="Search {entities}..."
        @create="handleCreate"
        @item-click="handleItemSelect"
      >
        <template #item="{ item, selected }">
          <{Entity}ListItem :{entity}="item" :selected="selected" />
        </template>
      </AdminSelectorPanel>
    </template>

    <!-- Right Panel: {Entity} Details -->
    <template #detail>
      <AdminDetailPanel
        v-model:mode="mode"
        :item="selected{Entity}"
        :loading="{entity}ListQuery.isLoading.value || is{Entity}DetailLoading"
        :can-edit="true"
        :can-delete="!selected{Entity}?.deleted_at"
        :can-restore="!!selected{Entity}?.deleted_at"
        :is-deleting="delete{Entity}Dialog.loading.value"
        :is-restoring="restore{Entity}Dialog.loading.value"
        create-button-label="Create New {Entity}"
        empty-title="No {entity} selected"
        empty-message="Select a {entity} from the list to view details"
        show-create-button
        @create="handleCreate"
        @edit="handleEdit"
        @delete="dialogs.openDialog('delete{Entity}')"
        @restore="dialogs.openDialog('restore{Entity}')"
        @cancel="handleEditCancel"
      >
        <!-- View Content -->
        <template #view="{ item }">
          <{Entity}ViewContent :{entity}="item" />
        </template>

        <!-- Edit Form -->
        <template #edit="{ item }">
          <Edit{Entity}Card :value="item.id" @cancel="handleEditCancel" @success="handleEditSuccess" />
        </template>

        <!-- Create Form -->
        <template #create>
          <Create{Entity}Card @cancel="handleEditCancel" @success="handleCreateSuccess" />
        </template>

        <!-- Sections (if entity has relations) -->
        <!-- Uncomment and customize if applicable:
        <template #sections="{ item }">
          <div class="sections-tabs">
            <button
              class="sections-tab"
              :class="{ 'sections-tab--active': currentSection === '{relation}' }"
              @click="currentSection = '{relation}'"
            >
              {Relation}s
              <span class="sections-tab__count">{{ item.{relation}s_count || 0 }}</span>
            </button>
          </div>

          <{Entity}{Relation}sSection
            v-if="currentSection === '{relation}'"
            :{entity}-id="item.id"
            :{relations}="{entity}{Relation}s"
            :loading="is{Relation}sLoading"
            :{relations}-count="item.{relations}_count"
            @add="show{Relation}Picker = true"
            @remove="(id, name) => dialogs.openDialog('remove{Relation}', { id, name })"
            @bulk-remove="handleBulkRemove{Relation}s"
          />
        </template>
        -->
      </AdminDetailPanel>
    </template>
  </AdminTwoPanelLayout>

  <!-- Dialogs -->
  <PremiumConfirmDialog
    v-model="delete{Entity}Dialog.show.value"
    title="Delete {Entity}"
    :message="`Delete '${selected{Entity}?.name}'? This will soft-delete the {entity}.`"
    variant="danger"
    :loading="delete{Entity}Dialog.loading.value"
    @confirm="handleDelete{Entity}"
    @cancel="dialogs.closeDialog('delete{Entity}')"
  />

  <PremiumConfirmDialog
    v-model="restore{Entity}Dialog.show.value"
    title="Restore {Entity}"
    :message="`Restore '${selected{Entity}?.name}'?`"
    variant="success"
    :loading="restore{Entity}Dialog.loading.value"
    @confirm="handleRestore{Entity}"
    @cancel="dialogs.closeDialog('restore{Entity}')"
  />

  <!-- Add relation dialogs and pickers if applicable:
  <PremiumConfirmDialog
    v-model="remove{Relation}Dialog.show.value"
    title="Remove {Relation}"
    :message="`Remove '{Relation}' from this {entity}?`"
    variant="danger"
    :loading="remove{Relation}Dialog.loading.value"
    @confirm="handleRemove{Relation}"
    @cancel="dialogs.closeDialog('remove{Relation}')"
  />

  <{Relation}PickerModal
    v-model="show{Relation}Picker"
    :excluded-{relation}-ids="excluded{Relation}Ids"
    @confirm="handleAdd{Relation}s"
  />
  -->
</template>

<style scoped>
/* Section Tabs - Only include if entity has relations */
/*
.sections-tabs {
  display: flex;
  gap: 0;
  border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.08);
}

.sections-tab {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 16px 24px;
  border: none;
  background: transparent;
  font-size: 0.875rem;
  font-weight: 500;
  color: rgba(var(--v-theme-on-surface), 0.6);
  cursor: pointer;
  position: relative;
  transition: all 0.15s ease;
}

.sections-tab:hover {
  color: rgba(var(--v-theme-on-surface), 0.8);
  background: rgba(var(--v-theme-on-surface), 0.02);
}

.sections-tab--active {
  color: rgb(var(--v-theme-primary));
}

.sections-tab--active::after {
  content: '';
  position: absolute;
  bottom: -1px;
  left: 0;
  right: 0;
  height: 2px;
  background: rgb(var(--v-theme-primary));
}

.sections-tab__count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 20px;
  height: 20px;
  padding: 0 6px;
  font-size: 0.6875rem;
  font-weight: 600;
  border-radius: 10px;
  background: rgba(var(--v-theme-on-surface), 0.08);
  color: rgba(var(--v-theme-on-surface), 0.6);
}

.sections-tab--active .sections-tab__count {
  background: rgba(var(--v-theme-primary), 0.1);
  color: rgb(var(--v-theme-primary));
}
*/
</style>
```

---

## Template: Page Wrapper

**File**: `resources/js/pages/app/admin/{category}/{entities}/{Entity}sPage.vue`

```vue
<script setup lang="ts">
/**
 * {Entity}sPage - Page wrapper for {entities} admin panel
 *
 * Simple wrapper that imports the layout.
 * Keep this minimal - all logic goes in the layout.
 */
import {Entity}sLayout from '@/layouts/app/admin/{category}/{entities}/{Entity}sLayout.vue';
</script>

<template>
  <{Entity}sLayout />
</template>
```

---

## Template: Create Card

**File**: `resources/js/components/local/app/admin/{category}/{entities}/Create{Entity}Card.vue`

```vue
<script setup lang="ts">
/**
 * Create{Entity}Card - Form for creating new {entities}
 */
import { ref } from 'vue';
import { useAdminCreate{Entity}Mutation } from '@/composables/mutations/admin/admin.{entity}.mutation';
import { useToast } from '@/toast-system/composables/useToast';
import FormPanel from '@/components/k/form/FormPanel.vue';
import KInput from '@/components/k/form/KInput.vue';
import KTextarea from '@/components/k/form/KTextarea.vue';
import KButton from '@/components/k/form/KButton.vue';

const emit = defineEmits<{
  cancel: [];
  success: [id: number];
}>();

// Form state
const name = ref('');
// Add more fields as needed

const toast = useToast();
const createMutation = useAdminCreate{Entity}Mutation();

async function handleSubmit() {
  if (!name.value.trim()) {
    toast.error('Name is required');
    return;
  }

  createMutation.mutate(
    {
      name: name.value.trim(),
      // Add more fields
    },
    {
      onSuccess: (response) => {
        toast.success('{Entity} created successfully');
        emit('success', response.data.id);
      },
      onError: () => {
        toast.error('Failed to create {entity}');
      },
    }
  );
}

function handleCancel() {
  emit('cancel');
}
</script>

<template>
  <FormPanel title="Create {Entity}">
    <form @submit.prevent="handleSubmit">
      <div class="form-fields">
        <KInput
          v-model="name"
          label="Name"
          placeholder="Enter {entity} name"
          :error="createMutation.validationErrors.value?.name?.[0]"
          required
        />
        <!-- Add more fields -->
      </div>

      <div class="form-actions">
        <KButton variant="ghost" @click="handleCancel">
          Cancel
        </KButton>
        <KButton
          type="submit"
          variant="primary"
          :loading="createMutation.isPending.value"
        >
          Create {Entity}
        </KButton>
      </div>
    </form>
  </FormPanel>
</template>

<style scoped>
.form-fields {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 24px;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 24px;
  border-top: 1px solid rgba(var(--v-theme-on-surface), 0.08);
}
</style>
```

---

## Template: Edit Card

**File**: `resources/js/components/local/app/admin/{category}/{entities}/Edit{Entity}Card.vue`

```vue
<script setup lang="ts">
/**
 * Edit{Entity}Card - Form for editing existing {entities}
 */
import { ref, watch, computed } from 'vue';
import { useAdmin{Entity}Query } from '@/composables/queries/admin/admin.{entity}.query';
import { useAdminUpdate{Entity}Mutation } from '@/composables/mutations/admin/admin.{entity}.mutation';
import { useToast } from '@/toast-system/composables/useToast';
import FormPanel from '@/components/k/form/FormPanel.vue';
import KInput from '@/components/k/form/KInput.vue';
import KTextarea from '@/components/k/form/KTextarea.vue';
import KButton from '@/components/k/form/KButton.vue';

interface Props {
  value: number; // {Entity} ID
}

const props = defineProps<Props>();

const emit = defineEmits<{
  cancel: [];
  success: [];
}>();

// Fetch current data
const {entity}Query = useAdmin{Entity}Query(computed(() => props.value));
const isLoading = computed(() => {entity}Query.isLoading.value);

// Form state
const name = ref('');
// Add more fields

// Populate form when data loads
watch(
  () => {entity}Query.data.value,
  (data) => {
    if (data) {
      name.value = data.name || '';
      // Populate more fields
    }
  },
  { immediate: true }
);

const toast = useToast();
const updateMutation = useAdminUpdate{Entity}Mutation();

async function handleSubmit() {
  if (!name.value.trim()) {
    toast.error('Name is required');
    return;
  }

  updateMutation.mutate(
    {
      id: props.value,
      name: name.value.trim(),
      // Add more fields
    },
    {
      onSuccess: () => {
        toast.success('{Entity} updated successfully');
        emit('success');
      },
      onError: () => {
        toast.error('Failed to update {entity}');
      },
    }
  );
}

function handleCancel() {
  emit('cancel');
}
</script>

<template>
  <FormPanel title="Edit {Entity}">
    <div v-if="isLoading" class="form-loading">
      Loading...
    </div>
    <form v-else @submit.prevent="handleSubmit">
      <div class="form-fields">
        <KInput
          v-model="name"
          label="Name"
          placeholder="Enter {entity} name"
          :error="updateMutation.validationErrors.value?.name?.[0]"
          required
        />
        <!-- Add more fields -->
      </div>

      <div class="form-actions">
        <KButton variant="ghost" @click="handleCancel">
          Cancel
        </KButton>
        <KButton
          type="submit"
          variant="primary"
          :loading="updateMutation.isPending.value"
        >
          Save Changes
        </KButton>
      </div>
    </form>
  </FormPanel>
</template>

<style scoped>
.form-loading {
  padding: 48px;
  text-align: center;
  color: rgba(var(--v-theme-on-surface), 0.5);
}

.form-fields {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 24px;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 24px;
  border-top: 1px solid rgba(var(--v-theme-on-surface), 0.08);
}
</style>
```

---

## Post-Generation Checklist

After generating files, perform these steps:

### 1. Update Barrel Export

Add to `resources/js/components/admin/index.ts`:

```typescript
// List Items
export { default as {Entity}ListItem } from './list-items/{Entity}ListItem.vue';
```

**Note:** The `AdminListItem` base component is already exported from `@/components/admin` via the molecules index. No additional exports needed for it.

### 2. Verify TypeScript Types

Ensure `resources/js/types/{entity}.type.ts` exports are correct and imported where needed.

### 3. Test API Endpoints

Run quick curl tests:
```bash
curl -X GET /api/admin/{entities}
curl -X GET /api/admin/{entities}/1
```

### 4. Build and Test

```bash
npm run build
npm run dev  # Test in browser
```

### 5. Clean Up

- Remove `public/hot` file after testing
- Kill any orphaned Node processes

---

## Customization Points

After generation, you may need to customize:

1. **Sort options** - Add entity-specific sorts
2. **Search fields** - Add fields to search
3. **List item display** - Adjust what's shown in the list
4. **View content sections** - Add/remove field groups
5. **Form fields** - Add entity-specific form inputs
6. **Validation** - Add client-side validation rules
7. **Relation tabs** - Add/remove relation sections

---

## Standard UI Patterns

### Empty State Pattern

When no item is selected in the detail panel, display a helpful empty state with:

1. **Title**: "No {entity} selected" (lowercase entity name)
2. **Message**: "Select a {entity} from the list to view details"
3. **Create Button**: Optionally show "Or create a new one" action (via `show-create-button` prop)

**CRITICAL - Empty Object Check:**
TanStack Query returns `{}` (empty object) as `placeholderData` when the query is disabled (no ID selected). Since empty objects are truthy, you MUST check for this in your `selected{Entity}` computed:

```typescript
// ✅ CORRECT - Checks for empty object
const selected{Entity} = computed(() => {
  const data = {entity}DetailQuery.data.value;
  // Check for empty object (query returns {} as placeholder when no id)
  // This ensures the empty state panel shows when no item is selected
  return data && Object.keys(data).length > 0 ? data : null;
});

// ❌ WRONG - Empty object {} is truthy, so empty state won't show
const selected{Entity} = computed(() => {entity}DetailQuery.data.value || null);
```

Without this check, the empty state panel won't display because `AdminDetailPanel` receives `{}` instead of `null`.

```vue
<AdminDetailPanel
  ...
  empty-title="No role selected"
  empty-message="Select a role from the list to view details"
  show-create-button
  ...
>
```

**Reference Implementations:**
- `RolesLayout.vue` - Full CRUD with abilities section
- `AccountsLayout.vue` - Full CRUD with roles/abilities/offices tabs
- `AbilitiesLayout.vue` - View-only (no create/edit, just disable/restore)

**For system-managed entities** (like Abilities), set `:show-create-button="false"` to hide the create option:

```vue
<AdminDetailPanel
  ...
  empty-title="No ability selected"
  empty-message="Select an ability from the list to view details"
  :show-create-button="false"
  ...
>
```

### Custom Header Pattern

For entities needing custom header displays (e.g., showing username, icon, code-style slug):

```vue
<AdminDetailPanel ...>
  <template #header="{ item }">
    <div class="custom-header">
      <div class="custom-header__icon">
        <v-icon :icon="item?.icon ?? 'mdi-default'" size="24" />
      </div>
      <div class="custom-header__content">
        <h2 class="custom-header__title">{{ item?.name }}</h2>
        <p class="custom-header__subtitle">{{ item?.description }}</p>
      </div>
      <div class="custom-header__actions">
        <!-- Action buttons -->
      </div>
    </div>
  </template>
</AdminDetailPanel>
```

**Reference:** See `AbilitiesLayout.vue` for icon-based header with monospace slug display.

### Custom Title/Subtitle Field Pattern

For entities where the default title (`name` or `title`) and subtitle (`slug`) don't apply, use the `title-field` and `subtitle-field` props:

```vue
<AdminDetailPanel
  ...
  title-field="particular"
  :subtitle-field="null"
  ...
>
```

| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `title-field` | `string` | undefined | Field name for title (defaults to `item.name \|\| item.title`) |
| `subtitle-field` | `string \| null` | undefined | Field name for subtitle (defaults to `item.slug`). Set to `null` to hide subtitle |

**Examples:**
- UACS entities: `title-field="particular"` `:subtitle-field="null"` - shows description as title, no subtitle
- Code-based entities: `title-field="code"` `subtitle-field="description"` - shows code as title

**Reference:** See `AccountGroupsLayout.vue` for usage with UACS entities.

### Section Tabs Pattern

For entities with multiple related data (e.g., User has Roles, Abilities, Offices):

```vue
<!-- Section Tabs -->
<div class="sections-tabs">
  <button
    class="sections-tab"
    :class="{ 'sections-tab--active': currentSection === 'roles' }"
    @click="currentSection = 'roles'"
  >
    <svg><!-- icon --></svg>
    Roles
    <span class="sections-tab__count">{{ item.roles_count || 0 }}</span>
  </button>
  <!-- More tabs... -->
</div>

<!-- Tab Content -->
<UserRolesSection v-if="currentSection === 'roles'" ... />
<UserAbilitiesSection v-if="currentSection === 'abilities'" ... />
```

**Key Features:**
- Lazy-load tab data (only fetch when tab is active)
- Show count badge on each tab
- Use consistent styling from AccountsLayout

**Reference:** See `AccountsLayout.vue` for full tabs implementation with lazy loading.

---

## Examples

### Basic Entity (no relations)

```
/admin-panel-integration Level --category=users
```

Generates: LevelsLayout, LevelListItem, LevelViewContent, queries, mutations

### Entity with Relations

```
/admin-panel-integration Office --category=offices --relations=users,documents
```

Generates: Full layout with Users and Documents tabs, picker modals, relation sections

### Custom Endpoint

```
/admin-panel-integration DocumentType --category=documents --endpoint=admin/doc-types
```

Uses `/api/admin/doc-types` instead of `/api/admin/document-types`
