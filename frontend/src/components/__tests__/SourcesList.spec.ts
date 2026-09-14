import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import SourcesList from '../SourcesList.vue'

describe('SourcesList', () => {
  it('viser dokument, afsnit og score uden filendelse', () => {
    const wrapper = mount(SourcesList, {
      props: { sources: [{ source: 'personalehaandbog.md', heading: 'Ferie', score: 0.7265 }] },
    })

    expect(wrapper.find('summary').text()).toBe('Kilder (1)')
    expect(wrapper.text()).toContain('personalehaandbog')
    expect(wrapper.text()).not.toContain('.md')
    expect(wrapper.text()).toContain('Ferie')
    expect(wrapper.text()).toContain('0,73')
  })

  it('rendrer intet uden kilder', () => {
    const wrapper = mount(SourcesList, { props: { sources: [] } })
    expect(wrapper.find('details').exists()).toBe(false)
  })
})
