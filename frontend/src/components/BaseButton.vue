<script setup lang="ts">
/**
 * Knap i ist.com's stil: pill, 2px kant, fed skrift. Fire varianter svarer til deres btn-primary
 * (mint), btn-secondary2 (mørk teal), btn-secondary3 (outline) og en dæmpet "ghost" til headeren.
 */
withDefaults(
  defineProps<{
    variant?: 'primary' | 'teal' | 'outline' | 'ghost'
    size?: 'md' | 'lg'
    type?: 'button' | 'submit'
    disabled?: boolean
    loading?: boolean
  }>(),
  { variant: 'primary', size: 'md', type: 'button', disabled: false, loading: false },
)
</script>

<template>
  <button
    :type="type"
    class="btn"
    :class="[`btn--${variant}`, `btn--${size}`, { 'btn--loading': loading }]"
    :disabled="disabled || loading"
    :aria-busy="loading || undefined"
  >
    <span v-if="loading" class="btn__spinner" aria-hidden="true"></span>
    <span class="btn__label"><slot /></span>
  </button>
</template>

<style scoped>
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: var(--btn-padding);
  border: var(--btn-border) solid transparent;
  border-radius: var(--radius-pill);
  font-weight: var(--btn-weight);
  font-size: 15px;
  line-height: 1;
  cursor: pointer;
  transition:
    background-color var(--transition),
    color var(--transition),
    border-color var(--transition),
    transform var(--transition);
  white-space: nowrap;
}

.btn--lg {
  padding: var(--btn-padding-lg);
  font-size: 16px;
}

.btn:not(:disabled):hover {
  transform: translateY(-1px);
}

.btn:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.btn--primary {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: var(--color-text);
}

.btn--primary:not(:disabled):hover {
  background: var(--color-secondary-1);
  border-color: var(--color-secondary-1);
  color: var(--color-text-inverse);
}

.btn--teal {
  background: var(--color-secondary-1);
  border-color: var(--color-secondary-1);
  color: var(--color-text-inverse);
}

.btn--teal:not(:disabled):hover {
  background: var(--color-secondary-1-hover);
  border-color: var(--color-secondary-1-hover);
}

.btn--outline {
  background: transparent;
  border-color: var(--color-secondary-1);
  color: var(--color-text);
}

.btn--outline:not(:disabled):hover {
  background: var(--color-secondary-1);
  color: var(--color-text-inverse);
}

.btn--ghost {
  background: transparent;
  border-color: rgba(255, 255, 255, 0.6);
  color: var(--nav-text);
}

.btn--ghost:not(:disabled):hover {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: var(--color-text);
}

.btn__spinner {
  width: 14px;
  height: 14px;
  border: 2px solid currentColor;
  border-right-color: transparent;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
