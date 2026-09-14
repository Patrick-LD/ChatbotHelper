<script setup lang="ts">
import { ref } from 'vue'
import BaseButton from '@/components/BaseButton.vue'

const props = defineProps<{ disabled?: boolean; placeholder?: string }>()
const emit = defineEmits<{ send: [text: string] }>()

const text = ref('')
const textarea = ref<HTMLTextAreaElement | null>(null)

function submit() {
  const value = text.value.trim()
  if (!value || props.disabled) return
  emit('send', value)
  text.value = ''
  resize()
  textarea.value?.focus()
}

/** Enter sender, Shift+Enter giver linjeskift — som i de fleste chat-vinduer. */
function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
    event.preventDefault()
    submit()
  }
}

function resize() {
  const el = textarea.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = `${Math.min(el.scrollHeight, 200)}px`
}

defineExpose({ focus: () => textarea.value?.focus() })
</script>

<template>
  <form class="composer" @submit.prevent="submit">
    <label for="composer-input" class="visually-hidden">Skriv en besked</label>
    <textarea
      id="composer-input"
      ref="textarea"
      v-model="text"
      class="composer__input"
      rows="1"
      :placeholder="placeholder ?? 'Skriv et spørgsmål eller en opgave…'"
      :disabled="disabled"
      autocomplete="off"
      @keydown="onKeydown"
      @input="resize"
    ></textarea>
    <BaseButton type="submit" variant="primary" :disabled="disabled || !text.trim()">Send</BaseButton>
  </form>
</template>

<style scoped>
.composer {
  display: flex;
  align-items: flex-end;
  gap: 12px;
  padding: 16px 20px;
  border-top: 1px solid var(--color-border);
  background: var(--color-bg);
}

.composer__input {
  flex: 1;
  min-height: 48px;
  max-height: 200px;
  padding: 12px 18px;
  border: 2px solid var(--color-border);
  border-radius: 24px;
  resize: none;
  background: var(--color-bg);
  line-height: 1.4;
}

.composer__input:focus {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 0.25rem rgba(201, 246, 220, 0.4);
  outline: 0;
}

.composer__input:disabled {
  background: var(--color-surface);
}
</style>
