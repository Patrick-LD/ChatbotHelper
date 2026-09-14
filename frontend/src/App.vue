<script setup lang="ts">
import { watch } from 'vue'
import { RouterView, useRouter } from 'vue-router'
import AppHeader from '@/components/AppHeader.vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'

const auth = useAuthStore()
const chat = useChatStore()
const router = useRouter()

// Forsvinder nøglen (log ud, eller API'et svarede 401), ryddes samtalen, og brugeren lander på login.
watch(
  () => auth.hasKey,
  (hasKey) => {
    if (!hasKey) {
      chat.reset()
      if (router.currentRoute.value.name !== 'login') {
        void router.push({ name: 'login' })
      }
    }
  },
)
</script>

<template>
  <div class="app">
    <a class="skip-link" href="#main">Spring til indhold</a>
    <AppHeader />
    <main id="main" class="app__main">
      <RouterView />
    </main>
  </div>
</template>

<style scoped>
.app {
  display: flex;
  flex-direction: column;
  min-height: 100%;
  background: var(--color-surface);
}

.app__main {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.skip-link {
  position: absolute;
  left: -999px;
  top: 8px;
  z-index: 100;
  padding: 8px 16px;
  background: var(--color-primary);
  border-radius: var(--radius-pill);
  font-weight: 700;
}

.skip-link:focus {
  left: 8px;
}
</style>
