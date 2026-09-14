<script setup lang="ts">
import { RouterLink } from 'vue-router'
import BaseButton from '@/components/BaseButton.vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'

const auth = useAuthStore()
const chat = useChatStore()

function newConversation() {
  chat.reset()
}

function logout() {
  auth.logout()
}
</script>

<template>
  <header class="header">
    <div class="container header__inner">
      <RouterLink to="/" class="header__brand" aria-label="IST Assistent — forside">
        <img src="/ist-logo.png" alt="IST" class="header__logo" width="46" height="26" />
        <span class="header__title">Assistent</span>
      </RouterLink>

      <div v-if="auth.hasKey" class="header__user">
        <span v-if="auth.user" class="header__name">
          {{ auth.displayName }}
          <span class="header__roles">
            <span v-for="role in auth.user.roles" :key="role" class="header__role">{{ role }}</span>
          </span>
        </span>
        <BaseButton variant="ghost" :disabled="!chat.hasMessages || chat.isSending" @click="newConversation">
          Ny samtale
        </BaseButton>
        <BaseButton variant="ghost" @click="logout">Log ud</BaseButton>
      </div>
    </div>
  </header>
</template>

<style scoped>
.header {
  background: var(--nav-bg);
  color: var(--nav-text);
}

.header__inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--gap);
  min-height: 72px;
  padding-block: 12px;
}

.header__brand {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  color: inherit;
  text-decoration: none;
}

/* Logoet er sort på transparent; på den sorte bjælke inverteres det til hvidt, som på ist.com's mørke flader. */
.header__logo {
  height: 26px;
  width: auto;
  filter: invert(1);
}

.header__title {
  font-family: var(--font-display);
  font-size: 26px;
  line-height: 1;
}

.header__user {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.header__name {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
}

.header__roles {
  display: inline-flex;
  gap: 4px;
}

.header__role {
  padding: 2px 8px;
  border-radius: var(--radius-pill);
  background: rgba(201, 246, 220, 0.18);
  color: var(--color-primary);
  font-size: 12px;
  font-weight: 700;
  text-transform: lowercase;
}

@media (max-width: 640px) {
  .header__name {
    display: none;
  }
}
</style>
